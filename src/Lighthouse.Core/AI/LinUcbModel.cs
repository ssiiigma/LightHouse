namespace Lighthouse.Core.AI;

/// <summary>Спостереження для повного перерахунку моделі: контекст, винагорода та вага (забування).</summary>
public sealed record WeightedObservation(double[] Context, double Reward, double Weight = 1.0);

/// <summary>
/// LinUCB із спільними параметрами (Li et al., 2010): оцінка винагороди θᵀx плюс бонус за невизначеність
/// α·√(xᵀA⁻¹x). Замість інверсії матриці на кожному кроці підтримується A⁻¹ і оновлюється за формулою
/// Шермана–Моррісона, тому Update має складність O(d²), а розмірність d невелика (≈8–12 ознак).
/// Для щотижневого перерахунку з вагами забування є Recompute (інверсія методом Гаусса–Жордана).
/// </summary>
public sealed class LinUcbModel
{
    private readonly double[][] _aInv;
    private readonly double[] _b;

    public int Dimension { get; }

    /// <summary>Коефіцієнт дослідження: більше значення — більше «цікавості» до невивчених контекстів.</summary>
    public double Alpha { get; }

    public LinUcbModel(int dimension, double alpha = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentOutOfRangeException.ThrowIfNegative(alpha);

        Dimension = dimension;
        Alpha = alpha;
        _b = new double[dimension];
        _aInv = new double[dimension][];
        for (var i = 0; i < dimension; i++)
        {
            _aInv[i] = new double[dimension];
            _aInv[i][i] = 1.0; // A = λI при λ = 1
        }
    }

    private LinUcbModel(LinUcbSnapshot snapshot) : this(snapshot.Dimension, snapshot.Alpha)
    {
        if (snapshot.AInv.Length != Dimension
            || snapshot.B.Length != Dimension
            || snapshot.AInv.Any(row => row.Length != Dimension))
        {
            throw new ArgumentException("Snapshot dimensions are inconsistent.", nameof(snapshot));
        }

        for (var i = 0; i < Dimension; i++)
        {
            Array.Copy(snapshot.AInv[i], _aInv[i], Dimension);
        }

        Array.Copy(snapshot.B, _b, Dimension);
    }

    public static LinUcbModel FromSnapshot(LinUcbSnapshot snapshot) => new(snapshot);

    public LinUcbSnapshot ToSnapshot() =>
        new(Dimension, Alpha, _aInv.Select(row => (double[])row.Clone()).ToArray(), (double[])_b.Clone());

    /// <summary>Вектор коефіцієнтів θ = A⁻¹b.</summary>
    public double[] Theta() => MultiplyMatrixVector(_aInv, _b);

    /// <summary>Очікувана винагорода θᵀx (без бонусу за дослідження).</summary>
    public double ExpectedReward(double[] context)
    {
        EnsureDimension(context);
        return Dot(Theta(), context);
    }

    /// <summary>Невизначеність √(xᵀA⁻¹x): зменшується, коли модель бачила схожі контексти.</summary>
    public double Uncertainty(double[] context)
    {
        EnsureDimension(context);
        var ax = MultiplyMatrixVector(_aInv, context);
        return Math.Sqrt(Math.Max(0.0, Dot(context, ax)));
    }

    /// <summary>Бонус за невизначеність α·√(xᵀA⁻¹x) — окремо від очікуваної винагороди (для пояснень).</summary>
    public double Bonus(double[] context) => Alpha * Uncertainty(context);

    /// <summary>Верхня довірча межа (UCB) — значення, за яким ранжуються завдання.</summary>
    public double Score(double[] context) => ExpectedReward(context) + Bonus(context);

    /// <summary>Повертає індекси контекстів у порядку спадання Score.</summary>
    public IReadOnlyList<int> RankDescending(IReadOnlyList<double[]> contexts) =>
        contexts
            .Select((context, index) => (Index: index, Score: Score(context)))
            .OrderByDescending(item => item.Score)
            .Select(item => item.Index)
            .ToList();

    /// <summary>Навчання на одному спостереженні: A⁻¹ ← A⁻¹ − (A⁻¹x)(A⁻¹x)ᵀ / (1 + xᵀA⁻¹x), b ← b + r·x.</summary>
    public void Update(double[] context, double reward)
    {
        EnsureDimension(context);

        var ax = MultiplyMatrixVector(_aInv, context);
        var denominator = 1.0 + Dot(context, ax);

        for (var i = 0; i < Dimension; i++)
        {
            for (var j = 0; j < Dimension; j++)
            {
                _aInv[i][j] -= ax[i] * ax[j] / denominator;
            }

            _b[i] += reward * context[i];
        }
    }

    /// <summary>
    /// Повний перерахунок: A = λI + Σ wᵢ xᵢxᵢᵀ, b = Σ wᵢ rᵢ xᵢ, потім A⁻¹ методом Гаусса–Жордана.
    /// Стан замінюється лише після успішної інверсії. Спостереження з вагою ≤ 0 пропускаються.
    /// </summary>
    public void Recompute(IEnumerable<WeightedObservation> observations, double lambda = LinUcbDefaults.Lambda)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lambda);

        var a = new double[Dimension][];
        for (var i = 0; i < Dimension; i++)
        {
            a[i] = new double[Dimension];
            a[i][i] = lambda;
        }

        var b = new double[Dimension];

        foreach (var observation in observations)
        {
            EnsureDimension(observation.Context);
            if (observation.Weight <= 0.0)
            {
                continue;
            }

            for (var i = 0; i < Dimension; i++)
            {
                for (var j = 0; j < Dimension; j++)
                {
                    a[i][j] += observation.Weight * observation.Context[i] * observation.Context[j];
                }

                b[i] += observation.Weight * observation.Reward * observation.Context[i];
            }
        }

        var inverse = Invert(a);
        for (var i = 0; i < Dimension; i++)
        {
            Array.Copy(inverse[i], _aInv[i], Dimension);
        }

        Array.Copy(b, _b, Dimension);
    }

    private void EnsureDimension(double[] context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != Dimension)
        {
            throw new ArgumentException(
                $"Context has {context.Length} features, expected {Dimension}.", nameof(context));
        }
    }

    private static double Dot(double[] left, double[] right)
    {
        var sum = 0.0;
        for (var i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

        private static double[] MultiplyMatrixVector(double[][] matrix, double[] vector)
    {
        var result = new double[vector.Length];
        for (var i = 0; i < matrix.Length; i++)
        {
            result[i] = Dot(matrix[i], vector);
        }

        return result;
    }

    /// <summary>Обернення методом Гаусса–Жордана з вибором головного елемента по стовпцю.</summary>
    private static double[][] Invert(double[][] matrix)
    {
        var n = matrix.Length;
        var a = new double[n][];
        var inverse = new double[n][];
        for (var i = 0; i < n; i++)
        {
            a[i] = (double[])matrix[i].Clone();
            inverse[i] = new double[n];
            inverse[i][i] = 1.0;
        }

        for (var column = 0; column < n; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < n; row++)
            {
                if (Math.Abs(a[row][column]) > Math.Abs(a[pivot][column]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(a[pivot][column]) < 1e-12)
            {
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");
            }

            (a[column], a[pivot]) = (a[pivot], a[column]);
            (inverse[column], inverse[pivot]) = (inverse[pivot], inverse[column]);

            var divisor = a[column][column];
            for (var j = 0; j < n; j++)
            {
                a[column][j] /= divisor;
                inverse[column][j] /= divisor;
            }

            for (var row = 0; row < n; row++)
            {
                if (row == column)
                {
                    continue;
                }

                var factor = a[row][column];
                if (factor == 0.0)
                {
                    continue;
                }

                for (var j = 0; j < n; j++)
                {
                    a[row][j] -= factor * a[column][j];
                    inverse[row][j] -= factor * inverse[column][j];
                }
            }
        }

        return inverse;
    }
}

namespace Lighthouse.Core.AI;

/// <summary>
/// LinUCB із спільними параметрами (Li et al., 2010): оцінка винагороди θᵀx плюс бонус за невизначеність
/// α·√(xᵀA⁻¹x). Замість інверсії матриці на кожному кроці підтримується A⁻¹ і оновлюється за формулою
/// Шермана–Моррісона, тому Update має складність O(d²), а розмірність d невелика (≈8–12 ознак).
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
            _aInv[i][i] = 1.0; // A = I на старті
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

    /// <summary>Очікувана винагорода θᵀx (без бонусу за дослідження).</summary>
    public double ExpectedReward(double[] context)
    {
        EnsureDimension(context);
        var theta = MultiplyMatrixVector(_aInv, _b);
        return Dot(theta, context);
    }

    /// <summary>Невизначеність √(xᵀA⁻¹x): зменшується, коли модель бачила схожі контексти.</summary>
    public double Uncertainty(double[] context)
    {
        EnsureDimension(context);
        var ax = MultiplyMatrixVector(_aInv, context);
        return Math.Sqrt(Math.Max(0.0, Dot(context, ax)));
    }

    /// <summary>Верхня довірча межа (UCB) — значення, за яким ранжуються завдання.</summary>
    public double Score(double[] context) => ExpectedReward(context) + Alpha * Uncertainty(context);

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
}

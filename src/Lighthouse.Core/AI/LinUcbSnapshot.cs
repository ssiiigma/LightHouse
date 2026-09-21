namespace Lighthouse.Core.AI;

/// <summary>Серіалізований стан моделі. Використовуються «зубчасті» масиви, щоб System.Text.Json зміг їх записати.</summary>
public sealed record LinUcbSnapshot(int Dimension, double Alpha, double[][] AInv, double[] B);

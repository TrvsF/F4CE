using NAudio.Wave;
using System;
using NCalc;

namespace F4CE.Objects;

internal partial class OFrequencyShiftSampleProvider : ISampleProvider
{
	public bool IsExpressionValid => CachedGoodExpression == WaveExpression;
	private string CachedGoodExpression = "";
	private string CachedBadExpression = "";
	private Expression Expression = null;

	private float EvaluateWave(float Frequency, float Time)
	{
		if (CachedGoodExpression == WaveExpression || CachedBadExpression == WaveExpression)
		{
			Expression.Parameters["f"] = Frequency;
			Expression.Parameters["t"] = Time;
			return Convert.ToSingle(Expression.Evaluate());
		}

		var NewExpression = new Expression(WaveExpression);

		NewExpression.Parameters["f"] = Frequency;
		NewExpression.Parameters["t"] = Time;
		NewExpression.Parameters["PI"] = MathF.PI;

		NewExpression.EvaluateFunction += (Expression, Args) =>
		{
			switch (Expression.ToLowerInvariant())
			{
				case "sin":
					Args.Result = MathF.Sin(Convert.ToSingle(Args.Parameters[0].Evaluate()));
					break;

				case "cos":
					Args.Result = MathF.Cos(Convert.ToSingle(Args.Parameters[0].Evaluate()));
					break;

				case "tan":
					Args.Result = MathF.Tan(Convert.ToSingle(Args.Parameters[0].Evaluate()));
					break;
				case "exp":
					Args.Result = MathF.Exp(Convert.ToSingle(Args.Parameters[0].Evaluate()));
					break;
				case "rnd":
					{
						float min = Convert.ToSingle(Args.Parameters[0].Evaluate());
						float max = Convert.ToSingle(Args.Parameters[1].Evaluate());

						float value = (float)(Random.Shared.NextDouble() * (max - min) + min);

						Args.Result = value;
						break;
					}
			}
		};

		if (NewExpression.HasErrors())
		{
			if (Expression == null)
			{
				return Frequency;
			}

			CachedBadExpression = WaveExpression;
			Expression.Parameters["f"] = Frequency;
			Expression.Parameters["t"] = Time;
			return Convert.ToSingle(Expression.Evaluate());
		}

		Expression = NewExpression;
		CachedGoodExpression = WaveExpression;
		return Convert.ToSingle(Expression.Evaluate());
	}
}
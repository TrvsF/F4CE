using NAudio.Wave;
using System;
using NCalc;

namespace F4CE.Objects;

internal partial class OSampleProviderOne : ISampleProvider
{
	public bool IsExpressionValid => CachedGoodExpression == PlaybackSettings.WaveExpression;

	private string CachedGoodExpression = "";
	private string CachedBadExpression = "";
	private Expression Expression = null;

	private float EvaluateWave(float Frequency, float Time)
	{
		string WaveExpression = PlaybackSettings.WaveExpression;

		if (CachedGoodExpression != WaveExpression && CachedBadExpression != WaveExpression)
		{
			Expression NewExpression = BuildExpression(WaveExpression);

			if (NewExpression.HasErrors())
			{
				CachedBadExpression = WaveExpression;
			}
			else
			{
				Expression = NewExpression;
				CachedGoodExpression = WaveExpression;
			}
		}

		if (Expression == null)
		{
			return Frequency;
		}

		Expression.Parameters["f"] = Frequency;
		Expression.Parameters["t"] = Time;
		return Convert.ToSingle(Expression.Evaluate());
	}

	private static Expression BuildExpression(string WaveExpression)
	{
		Expression NewExpression = new(WaveExpression);
		NewExpression.Parameters["PI"] = MathF.PI;

		NewExpression.EvaluateFunction += (Name, Args) =>
		{
			switch (Name.ToLowerInvariant())
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
						float Min = Convert.ToSingle(Args.Parameters[0].Evaluate());
						float Max = Convert.ToSingle(Args.Parameters[1].Evaluate());
						Args.Result = (float)(Random.Shared.NextDouble() * (Max - Min) + Min);
						break;
					}
			}
		};

		return NewExpression;
	}
}
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;

namespace F4CE.Objects;

public record FPlaybackSettings
{
	public string WaveExpression = "f*t";
	public bool Raw = false;
	public float TransposeSemitones = 0f;
	public float PlaybackSpeed = 1f;
	public float SilenceSeconds = 30f;
	public float PanBaseVolume = 0f;
	public float PanSpeed = 0f;
	public float Loudness = 1f;
	public int Rs = 3;
}

internal partial class OAudioPlayback
{
	public FPlaybackSettings PlaybackSettings { get; private set; } = new();
	public bool HasRecording { get => MemoryStream.Length > 0 && !IsRecording; }

	private readonly Guid ImGuiD = Guid.NewGuid();

	public void DrawBlock()
	{
		ImGui.PushID(ImGuiD.ToString());

		if (IsChild)
		{
			ImGui.Begin($"kidnamed{ImGuiD}");
		}

		if (!HasRecording)
		{
			if (!IsRecording)
			{
				if (ImGui.Button("Start Recording", new Vector2(160, 20)))
				{
					StartRecording();
				}
			}
			else
			{
				if (ImGui.Button("Stop Recording", new Vector2(160, 20)))
				{
					StopRecording();
				}
			}

			ImGui.SliderFloat("Silence Length (Seconds)", ref PlaybackSettings.SilenceSeconds, 0f, 120f);
			if (ImGui.Button("Create Silent Playback", new Vector2(160, 20)))
			{
				SetSilence(TimeSpan.FromSeconds(PlaybackSettings.SilenceSeconds));
				PlaybackSettings.WaveExpression = "sin(t*PI*100)";
			}
		}
		else
		{
			if (ImGui.Button("Clear"))
			{
				MemoryStream.SetLength(0);
			}

			ImGui.NewLine();
			ImGui.NewLine();

			if (!IsPlaying)
			{
				if (ImGui.Button($"Play Recording", new Vector2(120, 20)))
				{
					PlayRecording();
				}
			}
			else
			{
				if (ImGui.Button($"Stop", new Vector2(120, 20)))
				{
					StopPlayback();
				}
			}
			ImGui.SameLine();
			ImGui.Text($"{GetDuration().TotalSeconds}s");
			ImGui.SameLine();
			ImGui.Checkbox("Raw", ref PlaybackSettings.Raw);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PanSpeed", ref PlaybackSettings.PanSpeed, 0f, 20f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PlaybackSpeed", ref PlaybackSettings.PlaybackSpeed, 0.1f, 5f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderInt("Rs", ref PlaybackSettings.Rs, 0, 8);
			ImGui.SameLine();
			if (ImGui.Button("ADD!"))
			{
				CreateChildPlayback();
			}

			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("Transpose", ref PlaybackSettings.TransposeSemitones, -12f, 12f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("Loudness", ref PlaybackSettings.Loudness, 0f, 4f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PanBaseVolume", ref PlaybackSettings.PanBaseVolume, 0f, 1f);
			ImGui.SameLine();

			//if (IsInputValid)
			//{
			//	ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0f, 0f, 1f));
			//}
			ImGui.SetNextItemWidth(320);
			ImGui.InputText("Expression", ref PlaybackSettings.WaveExpression, 1024);
			ImGui.SameLine();
			//if (IsInputValid)
			//{
			//	ImGui.PopStyleColor();
			//}

			//float[] Waveform = GetWaveform(480, 3.0f);
			//Window.DrawWaveform(Waveform, new Vector2(480, 80));

			RefreshSettings();
		}

		if (IsChild)
		{
			if (ImGui.Button("Beam me UP"))
			{
				MergeRequested.Invoke(this, TimeSpan.FromSeconds(5));
			}
			ImGui.SameLine();
			if (ImGui.Button("FUCK me"))
			{
				Window.RemovePlayback(this);
			}
			ImGui.End();
		}

		ImGui.PopID();
	}

	private void CreateChildPlayback()
	{
		OAudioPlayback ChildPlayback = new()
		{
			IsChild = true,
		};

		ChildPlayback.MergeRequested += RequestAddition;
		Window.AddPlayback(ChildPlayback);
	}
}

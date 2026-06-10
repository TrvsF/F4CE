using ImGuiNET;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	public string WaveExpression = "f*t";
	public bool Raw = false;
	public float PlaybackSpeed = 1f;
	public float PanSpeed = 0f;
	public float Transpose = 0f;
	public float SilenceSeconds = 30f;
	public int Rs = 3;

	private readonly Guid ImGuiD = Guid.NewGuid();

	public bool HasRecording { get => MemoryStream.Length > 0 && !IsRecording; }

	public void DrawBlock()
	{
		ImGui.PushID(ImGuiD.ToString());

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

			ImGui.SliderFloat("Silence Length (Seconds)", ref SilenceSeconds, 0f, 120f);
			if (ImGui.Button("Create Silent Playback", new Vector2(160, 20)))
			{
				SetSilence(TimeSpan.FromSeconds(SilenceSeconds));
			}

			ImGui.PopID();
			return;
		}

		if (ImGui.Button("Clear"))
		{
			MemoryStream.SetLength(0);
		}

		ImGui.NewLine();
		ImGui.NewLine();

		if (!IsPlaying)
		{
			if (ImGui.Button($"Play Recording", new Vector2(240, 80)))
			{
				PlayRecording();
			}
		}
		else
		{
			if (ImGui.Button($"Stop", new Vector2(240, 80)))
			{
				StopPlayback();
			}
		}
		ImGui.SameLine();
		ImGui.Checkbox("Raw", ref Raw);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(80);
		ImGui.SliderFloat("PanSpeed", ref PanSpeed, 0f, 20f);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(80);
		ImGui.SliderFloat("PlaybackSpeed", ref PlaybackSpeed, 0.1f, 5f);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(80);
		ImGui.SliderInt("Rs", ref Rs, 0, 8);
		ImGui.SetNextItemWidth(80);
		ImGui.SliderFloat("Transpose", ref Transpose, -12f, 12f);
		ImGui.SameLine();

		if (IsInputValid)
		{
			ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0f, 0f, 1f));
		}
		ImGui.SetNextItemWidth(400);
		ImGui.InputText("Expression", ref WaveExpression, 1024);
		ImGui.SameLine();
		if (IsInputValid)
		{
			ImGui.PopStyleColor();
		}

		float[] Waveform = GetWaveform(480, 3.0f);
		Window.DrawWaveform(Waveform, new Vector2(480, 80));

		SetProviderSettings();

		ImGui.PopID();
	}
}

using ImGuiNET;
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
	public bool HasRecording { get => MemoryStream.Length > 0 && !IsRecording; }

	private readonly Guid ImGuiD = Guid.NewGuid();

	private float SilenceSeconds = 30f;

	public void DrawBlock()
	{
		ImGui.PushID(ImGuiD.ToString());

		if (!HasRecording)
		{
			ImGui.InputFloat("Silence Length (Seconds)", ref SilenceSeconds);

			if (ImGui.Button("Create Silent Playback", new Vector2(160, 40)))
			{
				SetSilence(TimeSpan.FromSeconds(SilenceSeconds));
			}
			else
			{
				if (!IsRecording)
				{
					if (ImGui.Button("Start Recording", new Vector2(160, 40)))
					{
						StartRecording();
					}
				}
				else
				{
					if (ImGui.Button("Stop Recording", new Vector2(160, 40)))
					{
						StopRecording();
					}
				}
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
			if (ImGui.Button($"Play Recording", new Vector2(240, 40)))
			{
				PlayRecording();
			}

			if (ImGui.Button("Play Noise"))
			{
				PlaySWave();
			}
		}
		else
		{
			if (ImGui.Button($"Stop", new Vector2(240, 40)))
			{
				StopPlayback();
			}
		}

		float[] Waveform = GetWaveform(480, 3.0f);
		Window.DrawWaveform(Waveform, new Vector2(480, 100));

		ImGui.PopID();
	}
}

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
	public readonly Guid ImGuiD = Guid.NewGuid();

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

			ImGui.Text($"{Children.Count}kidz");
			ImGui.SameLine();

			if (!IsPlaying)
			{
				if (ImGui.Button($"Play Recording", new Vector2(120, 20)))
				{
					StartPlayback();
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
			ImGui.Text($"{GetTotalDuration().TotalSeconds}s");
			ImGui.SameLine();

			//if (IsInputValid)
			//{
			//	ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0f, 0f, 1f));
			//}
			ImGui.SetNextItemWidth(320);
			ImGui.InputText("Expression", ref PlaybackSettings.WaveExpression, 1024);
			//if (IsInputValid)
			//{
			//	ImGui.PopStyleColor();
			//}

			if (!IsRecording)
			{
				const float PixelsPerSecond = 20f;
				const float MainHeight = 40f;
				const float ChildHeight = 28f;
				const float ChildGap = 4f;

				var DrawList = ImGui.GetWindowDrawList();
				var TimelineOrigin = ImGui.GetCursorScreenPos();
				var TotalWidth = PixelsPerSecond * (float)GetTotalDuration().TotalSeconds;

				var MainColour = ImGui.GetColorU32(new Vector4(0.20f, 0.55f, 0.95f, 1.0f));
				DrawList.AddRectFilled(TimelineOrigin, TimelineOrigin + new Vector2(TotalWidth, MainHeight), MainColour);

				var ChildColour = ImGui.GetColorU32(new Vector4(0.25f, 0.72f, 0.45f, 1.0f));
				float ChildRowY = TimelineOrigin.Y + MainHeight + ChildGap;

				float ChildOffsetY = 0f;
				foreach (var (Child, EmplaceTime) in Children)
				{
					if (!Child.HasRecording)
					{
						continue;
					}

					float ChildStartX = TimelineOrigin.X + PixelsPerSecond * (float)EmplaceTime.TotalSeconds;
					float ChildWidth = PixelsPerSecond * (float)Child.GetTotalDuration().TotalSeconds;

					var ChildPos = new Vector2(ChildStartX, ChildRowY + ChildOffsetY);
					DrawList.AddRectFilled(ChildPos, ChildPos + new Vector2(ChildWidth, ChildHeight), ChildColour);
					ChildOffsetY += ChildHeight + ChildGap;
				}

				float TotalHeight = MainHeight + ChildOffsetY;

				if (IsPlaying)
				{
					float CursorX = TimelineOrigin.X + float.Lerp(0f, TotalWidth, PlaybackProgress);
					var CursorPos = new Vector2(CursorX, TimelineOrigin.Y);
					DrawList.AddRectFilled(CursorPos, CursorPos + new Vector2(3f, TotalHeight), ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1.0f)));
				}

				ImGui.Dummy(new Vector2(TotalWidth, TotalHeight));
			}

			foreach (var (_, _) in Children)
			{
				ImGui.NewLine();
			}

			RefreshSettings();
		}

		if (ImGui.Button("save"))
		{
			if (!Window.StoredPlaybacks.Contains(this))
			{
				Window.StoredPlaybacks.Add(this);
			}
		}

		if (IsChild)
		{
			ImGui.NewLine();
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

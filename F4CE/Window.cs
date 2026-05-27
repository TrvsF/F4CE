using F4CE.Backends;
using F4CE.Objects;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace F4CE;

internal class Window : GameWindow
{
	public Window() : base(GameWindowSettings.Default, new NativeWindowSettings() { ClientSize = new OpenTK.Mathematics.Vector2i(1280, 960), APIVersion = new Version(3, 3) })
	{ }

	protected override void OnLoad()
	{
		base.OnLoad();

		Title = $"F4CE ({GL.GetString(StringName.Version)})";

		GL.DebugMessageCallback(DebugProcCallback, IntPtr.Zero);
		GL.Enable(EnableCap.DebugOutput);
		GL.Enable(EnableCap.DebugOutputSynchronous);

		ImGui.CreateContext();
		ImGuiIOPtr IO = ImGui.GetIO();
		IO.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
		IO.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
		IO.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
		IO.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

		ImGui.StyleColorsDark();

		ImGuiStylePtr Style = ImGui.GetStyle();
		if ((IO.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
		{
			Style.WindowRounding = 0.0f;
			Style.Colors[(int)ImGuiCol.WindowBg].W = 1.0f;
		}

		ImguiImplOpenTK4.Init(this);
		ImguiImplOpenGL3.Init();
	}

	protected override void OnRenderFrame(FrameEventArgs EventArgs)
	{
		base.OnRenderFrame(EventArgs);

		ImguiImplOpenGL3.NewFrame();
		ImguiImplOpenTK4.NewFrame();
		ImGui.NewFrame();

		ImGui.DockSpaceOverViewport();
		ImGui.ShowDemoWindow();

		DrawMainImgui();

		ImGui.Render();
		GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
		GL.ClearColor(new OpenTK.Mathematics.Color4(0, 32, 48, 255));
		GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		ImguiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

		if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
		{
			ImGui.UpdatePlatformWindows();
			ImGui.RenderPlatformWindowsDefault();
			Context.MakeCurrent();
		}

		SwapBuffers();
	}

	private readonly List<OAudioPlayback> StoredPlaybacks = new();
	private OAudioPlayback CurrentPlayback = null;
	private float SilenceSeconds = 1.0f;

	private void DrawMainImgui()
	{
		ImGui.Begin("main");

		CurrentPlayback ??= new OAudioPlayback();

		ImGui.InputFloat("Silence Length (Seconds)", ref SilenceSeconds);

		if (ImGui.Button("Create Silent Playback", new Vector2(160, 40)))
		{
			CurrentPlayback.SetSilence(TimeSpan.FromSeconds(SilenceSeconds));

			StoredPlaybacks.Add(CurrentPlayback);
			CurrentPlayback = null;
		}
		else
		{
			if (!CurrentPlayback.IsRecording)
			{
				if (ImGui.Button("Start Recording", new Vector2(160, 40)))
				{
					CurrentPlayback.StartRecording();
				}
			}
			else
			{
				if (ImGui.Button("Stop Recording", new Vector2(160, 40)))
				{
					CurrentPlayback.StopRecording();
					StoredPlaybacks.Add(CurrentPlayback);
					CurrentPlayback = null;
				}
			}
		}

		ImGui.NewLine();

		int Count = 0;
		foreach (var Playback in StoredPlaybacks)
		{
			ImGui.SameLine();
			float Length = Playback.Length * 0.001f;
			if (!Playback.IsPlaying)
			{
				if (ImGui.Button($"Track {Count}", new Vector2(Length, 40)))
				{
					Playback.PlayRecording();
				}
			}
			else
			{
				if (ImGui.Button($"GO {Count}", new Vector2(Length, 40)))
				{
					Playback.StopPlayback();
				}
			}

			int WaveLength = (int)(Playback.Length * 0.0005f);
			float[] Waveform = Playback.GetWaveform(WaveLength * 10, 3.0f);
			DrawWaveform(Waveform, new Vector2(WaveLength, 100));

			++Count;
		}

		if (ImGui.Button("Play All"))
		{
			OAudioPlayback Playback = OAudioPlayback.CombinePlaybacks(StoredPlaybacks); // TODO : makea ref
			Playback.PlaySWave();
		}

		ImGui.End();
	}

	public void OnClosed()
	{
		ImguiImplOpenGL3.Shutdown();
		ImguiImplOpenTK4.Shutdown();
	}

	private readonly Random RandomImGuiIdChrist = new();

	public void DrawWaveform(float[] Samples, Vector2 Size)
	{
		ImDrawListPtr DrawList = ImGui.GetWindowDrawList();
		Vector2 Pos = ImGui.GetCursorScreenPos();

		ImGui.InvisibleButton($"Waveform{RandomImGuiIdChrist.NextSingle()}", Size);

		float MidY = Pos.Y + Size.Y * 0.5f;

		for (int Sample = 1; Sample < Samples.Length; Sample++)
		{
			float X1 = Pos.X + ((Sample - 1) / (float)Samples.Length) * Size.X;
			float X2 = Pos.X + (Sample / (float)Samples.Length) * Size.X;

			float Y1 = MidY - Samples[Sample - 1] * Size.Y * 0.5f;
			float Y2 = MidY - Samples[Sample] * Size.Y * 0.5f;

			DrawList.AddLine(new Vector2(X1, Y1), new Vector2(X2, Y2), ImGui.GetColorU32(ImGuiCol.PlotLines), 1.5f);
		}
	}

	public readonly static DebugProc DebugProcCallback = Window_DebugProc;
	private static void Window_DebugProc(DebugSource Source, DebugType Type, int Id, DebugSeverity Severity, int Length, IntPtr PtrMessage, IntPtr PtrInt)
	{
		var ParsedMessage = Marshal.PtrToStringAnsi(PtrMessage, Length);
		var ShowMessage = true;

		switch (Source)
		{
			case DebugSource.DebugSourceApplication:
				ShowMessage = false;
				break;
			case DebugSource.DontCare:
			case DebugSource.DebugSourceApi:
			case DebugSource.DebugSourceWindowSystem:
			case DebugSource.DebugSourceShaderCompiler:
			case DebugSource.DebugSourceThirdParty:
			case DebugSource.DebugSourceOther:
			default:
				ShowMessage = true;
				break;
		}

		if (!ShowMessage)
		{
			return;
		}

		switch (Severity)
		{
			case DebugSeverity.DontCare:
				Console.WriteLine($"[DontCare] [{Source}] {ParsedMessage}");
				break;
			case DebugSeverity.DebugSeverityHigh:
				Console.Error.WriteLine($"Error: [{Source}] {ParsedMessage}");
				break;
			case DebugSeverity.DebugSeverityMedium:
				Console.WriteLine($"Warning: [{Source}] {ParsedMessage}");
				break;
			case DebugSeverity.DebugSeverityLow:
				Console.WriteLine($"Info: [{Source}] {ParsedMessage}");
				break;
			case DebugSeverity.DebugSeverityNotification:
				//Console.WriteLine($"[Fuck THis] [{Source}] {ParsedMessage}");
				break;
			default:
				Console.WriteLine($"[{Severity}] [{Source}] {ParsedMessage}");
				break;
		}
	}
}

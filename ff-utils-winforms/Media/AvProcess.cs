﻿using Nmkoder.Data;
using Nmkoder.Extensions;
using Nmkoder.IO;
using Nmkoder.Main;
using Nmkoder.OS;
using Nmkoder.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nmkoder.Media
{
    class AvProcess
    {
        public static Process lastAvProcess;
        public static Stopwatch timeSinceLastOutput = new Stopwatch();
        public enum TaskType { ExtractFrames, ExtractOther, Encode, GetInfo, Merge, Other };
        public static TaskType lastTask = TaskType.Other;

        public static string lastOutputFfmpeg;

        public enum LogMode { Visible, OnlyLastLine, Hidden }
        static LogMode currentLogMode;
        static bool showProgressBar;

        static readonly string defLogLevel = "warning";

        public static void Kill()
        {
            if (lastAvProcess == null) return;

            try
            {
                OsUtils.KillProcessTree(lastAvProcess.Id);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to kill lastAvProcess process tree: {e.Message}", true);
            }
        }

        public static async Task RunFfmpeg(string args, LogMode logMode, TaskType taskType = TaskType.Other, bool progressBar = false)
        {
            await RunFfmpeg(args, "", logMode, defLogLevel, taskType, progressBar);
        }

        public static async Task RunFfmpeg(string args, LogMode logMode, string loglevel, TaskType taskType = TaskType.Other, bool progressBar = false)
        {
            await RunFfmpeg(args, "", logMode, loglevel, taskType, progressBar);
        }

        public static async Task RunFfmpeg(string args, string workingDir, LogMode logMode, TaskType taskType = TaskType.Other, bool progressBar = false)
        {
            await RunFfmpeg(args, workingDir, logMode, defLogLevel, taskType, progressBar);
        }

        public static async Task RunFfmpeg(string args, string workingDir, LogMode logMode, string loglevel, TaskType taskType = TaskType.Other, bool progressBar = false)
        {
            lastOutputFfmpeg = "";
            currentLogMode = logMode;
            showProgressBar = progressBar;
            Process ffmpeg = OsUtils.NewProcess(true);
            timeSinceLastOutput.Restart();
            lastAvProcess = ffmpeg;
            lastTask = taskType;

            if (string.IsNullOrWhiteSpace(loglevel))
                loglevel = defLogLevel;

            string beforeArgs = $"-hide_banner -stats -loglevel {loglevel} -y";

            if (!string.IsNullOrWhiteSpace(workingDir))
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {workingDir.Wrap()} & {Path.Combine(GetDir(), "ffmpeg.exe").Wrap()} {beforeArgs} {args}";
            else
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetDir().Wrap()} & ffmpeg.exe {beforeArgs} {args}";

            if (logMode != LogMode.Hidden) Logger.Log("Running FFmpeg...", false);
            Logger.Log($"ffmpeg {beforeArgs} {args}", true, false, "ffmpeg");
            ffmpeg.OutputDataReceived += FfmpegOutputHandler;
            ffmpeg.ErrorDataReceived += FfmpegOutputHandler;
            ffmpeg.Start();
            ffmpeg.BeginOutputReadLine();
            ffmpeg.BeginErrorReadLine();

            while (!ffmpeg.HasExited)
                await Task.Delay(1);

            if (progressBar)
                Program.mainForm.SetProgress(0);
        }

        static void FfmpegOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            timeSinceLastOutput.Restart();

            if (RunTask.canceled || outLine == null || outLine.Data == null)
                return;

            string line = outLine.Data;
            lastOutputFfmpeg = lastOutputFfmpeg + "\n" + line;

            bool hidden = currentLogMode == LogMode.Hidden;

            if (HideMessage(line)) // Don't print certain warnings 
                hidden = true;

            bool replaceLastLine = currentLogMode == LogMode.OnlyLastLine;

            if (line.StartsWith("frame="))
                line = FormatUtils.BeautifyFfmpegStats(line);

            Logger.Log(line, hidden, replaceLastLine, "ffmpeg");

            if (line.Contains(".srt: Invalid data found"))
                Logger.Log($"Warning: Failed to encode subtitle track {line.Split(':')[2]}. This track will be missing in the output file.");

            if (line.Contains("Could not open file"))
                RunTask.Cancel($"FFmpeg Error: {line}");

            if (line.Contains("No NVENC capable devices found") || line.MatchesWildcard("*nvcuda.dll*"))
                RunTask.Cancel($"FFmpeg Error: {line}\nMake sure you have an NVENC-capable Nvidia GPU.");

            if (!hidden && showProgressBar && line.Contains("Time:"))
            {
                Regex timeRegex = new Regex("(?<=Time:).*(?= )");
                UpdateFfmpegProgress(timeRegex.Match(line).Value);
            }
        }

        static bool HideMessage(string msg)
        {
            string[] hiddenMsgs = new string[] { "can produce invalid output", "pixel format", "provided invalid" };

            foreach (string str in hiddenMsgs)
                if (msg.MatchesWildcard($"*{str}*"))
                    return true;

            return false;
        }

        public static async Task<string> GetFfmpegOutputAsync(string args, bool setBusy = false, bool progressBar = false)
        {
            timeSinceLastOutput.Restart();
            if (Program.busy) setBusy = false;
            lastOutputFfmpeg = "";
            showProgressBar = progressBar;
            Process ffmpeg = OsUtils.NewProcess(true);
            lastAvProcess = ffmpeg;
            ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetDir().Wrap()} & ffmpeg.exe -hide_banner -y -stats {args}";
            Logger.Log($"ffmpeg {args}", true, false, "ffmpeg");
            if (setBusy) Program.mainForm.SetWorking(true);
            lastOutputFfmpeg = await OsUtils.GetOutputAsync(ffmpeg);
            while (!ffmpeg.HasExited) await Task.Delay(50);
            while (timeSinceLastOutput.ElapsedMilliseconds < 200) await Task.Delay(50);
            if (setBusy) Program.mainForm.SetWorking(false);
            return lastOutputFfmpeg;
        }

        public static string GetFfprobeOutput(string args)
        {
            Process ffprobe = OsUtils.NewProcess(true);
            ffprobe.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetDir().Wrap()} & ffprobe.exe {args}";
            Logger.Log($"ffprobe {args}", true, false, "ffmpeg");
            ffprobe.Start();
            ffprobe.WaitForExit();
            string output = ffprobe.StandardOutput.ReadToEnd();
            string err = ffprobe.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err)) output += "\n" + err;
            return output;
        }

        public static void UpdateFfmpegProgress(string ffmpegTime)
        {
            try
            {
                Form1 form = Program.mainForm;
                //long currInDuration = (form.currInDurationCut < form.currInDuration) ? form.currInDurationCut : form.currInDuration;
                long currInDuration = 1000 * 60 * 60 * 1; // TODO: Use actual duration instead of 1h placeholder

                if (currInDuration < 1)
                {
                    Program.mainForm.SetProgress(0);
                    return;
                }

                long total = currInDuration / 100;
                long current = FormatUtils.TimestampToMs(ffmpegTime);
                int progress = Convert.ToInt32(current / total);
                Program.mainForm.SetProgress(progress);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to get ffmpeg progress: {e.Message}", true);
            }
        }

        static string GetDir()
        {
            return Paths.GetBinPath();
        }

        static string GetCmdArg()
        {
            return "/C";
        }

        public static async Task SetBusyWhileRunning()
        {
            if (Program.busy) return;

            await Task.Delay(100);
            while (!lastAvProcess.HasExited)
                await Task.Delay(10);
        }
    }
}

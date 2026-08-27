// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Configuration;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Render;
using osu.Framework.Threading;
using osu.Framework.Timing;

namespace osu.Framework.Platform
{
    public abstract class DesktopGameHost : SDLGameHost
    {
        private NamedPipeIpcProvider ipcProvider;
        private readonly string ipcPipeName;

        private FramedClock customClock;

        protected DesktopGameHost(string gameName, HostOptions options = null)
            : base(gameName, options)
        {
            ipcPipeName = Options.IPCPipeName;
            IsPortableInstallation = Options.PortableInstallation;
        }

        protected sealed override Storage GetDefaultGameStorage()
        {
            if (IsPortableInstallation || File.Exists(Path.Combine(RuntimeInfo.StartupDirectory, FrameworkConfigManager.FILENAME)))
                return GetStorage(RuntimeInfo.StartupDirectory);

            return base.GetDefaultGameStorage();
        }

        public sealed override Storage GetStorage(string path) => new DesktopStorage(path, this);

        public override bool IsPrimaryInstance
        {
            get
            {
                // make sure we have actually attempted to bind IPC as this call may occur before the host is run.
                ensureIPCReady();

                return base.IsPrimaryInstance;
            }
        }

        protected override IFrameBasedClock SceneGraphClock
            => customClock ?? base.SceneGraphClock;

        protected override void UpdateFrame()
        {
            customClock?.ProcessFrame();
            base.UpdateFrame();
        }

        protected override void SetupForRun()
        {
            ensureIPCReady();

            MaximumDrawHz = 0;

            DebugUtils.RealtimeClock = new HeadlessGameHost.FastClock(HeadlessGameHost.CLOCK_RATE, Threads.ToArray());
            customClock = new FramedClock(DebugUtils.RealtimeClock);

            // time is incremented per frame, rather than based on the real-world time.
            // therefore our goal is to run frames as fast as possible.
            MaximumUpdateHz = MaximumInactiveHz = 0;
        }

        protected override ThreadRunner CreateThreadRunner(InputThread mainThread) => new RenderThreadRunner(mainThread, RenderFramerate.Fps60);

        private void ensureIPCReady()
        {
            if (ipcPipeName == null)
                return;

            if (ipcProvider != null)
                return;

            ipcProvider = new NamedPipeIpcProvider(ipcPipeName);
            ipcProvider.MessageReceived += OnMessageReceived;

            IsPrimaryInstance = ipcProvider.Bind();
        }

        public bool IsPortableInstallation { get; }

        public override bool OpenFileExternally(string filename)
        {
            openUsingShellExecute(filename);
            return true;
        }

        public override void OpenUrlExternally(string url)
        {
            if (!url.CheckIsValidUrl())
                throw new ArgumentException("The provided URL must be one of either http://, https:// or mailto: protocols.", nameof(url));

            try
            {
                openUsingShellExecute(url);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to open external link.");
            }
        }

        public override bool PresentFileExternally(string filename)
        {
            // should be overriden to highlight/select the file in the folder if such native API exists.
            OpenFileExternally(Path.GetDirectoryName(filename.TrimDirectorySeparator()));
            return true;
        }

        private static void openUsingShellExecute(string path) => Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true //see https://github.com/dotnet/corefx/issues/10361
        });

        public override Task SendMessageAsync(IpcMessage message)
        {
            ensureIPCReady();

            return ipcProvider.SendMessageAsync(message);
        }

        public override Task<IpcMessage> SendMessageWithResponseAsync(IpcMessage message)
        {
            ensureIPCReady();

            return ipcProvider.SendMessageWithResponseAsync(message);
        }

        protected override void Dispose(bool isDisposing)
        {
            ipcProvider?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}

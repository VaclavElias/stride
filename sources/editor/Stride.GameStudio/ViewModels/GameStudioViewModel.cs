// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModel;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.MostRecentlyUsedFiles;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Services;
using Stride.Core.Translation;
using Stride.Assets.Effect;
using Stride.Assets.Presentation.ViewModel;
using Stride.Core.CodeEditorSupport;
using Stride.GameStudio.Services;
using Stride.GameStudio.Helpers;
using Stride.Core.Presentation.ViewModels;

namespace Stride.GameStudio.ViewModels
{
    public class GameStudioViewModel : EditorViewModel
    {
        private PreviewViewModel preview;
        private DebuggingViewModel debugging;
        private string restartArguments;
        private readonly List<IDEInfo> availableIDEs;

        public GameStudioViewModel([NotNull] IViewModelServiceProvider serviceProvider, MostRecentlyUsedFileCollection mru)
            : base(serviceProvider, mru, StrideGameStudio.EditorName, StrideGameStudio.EditorVersionMajor)
        {
            Panels = new EditionPanelViewModel(ServiceProvider);
            availableIDEs = [IDEInfo.DefaultIDE];
            availableIDEs.AddRange(IDEInfoVersions.AvailableIDEs());
            NewSessionCommand = new AnonymousCommand(serviceProvider, RestartAndCreateNewSession);
            OpenAboutPageCommand = new AnonymousCommand(serviceProvider, OpenAboutPage);
            OpenSessionCommand = new AnonymousTaskCommand<UFile>(serviceProvider, RestartAndOpenSession);
            ReloadSessionCommand = new AnonymousTaskCommand(serviceProvider, () => RestartAndOpenSession(Session.SessionFilePath));
        }

        public static GameStudioViewModel GameStudio => (GameStudioViewModel)Instance;

        [NotNull]
        public EditionPanelViewModel Panels { get; }

        public StrideAssetsViewModel StrideAssets => StrideAssetsViewModel.Instance;

        public PreviewViewModel Preview { get => preview; set => SetValue(ref preview, value); }

        public DebuggingViewModel Debugging { get => debugging; set => SetValue(ref debugging, value); }

        [NotNull]
        public IReadOnlyList<IDEInfo> AvailableIDEs => availableIDEs;

        [NotNull]
        public ICommandBase NewSessionCommand { get; }

        [NotNull]
        public ICommandBase OpenAboutPageCommand { get; }

        [NotNull]
        public ICommandBase OpenSessionCommand { get; }

        [NotNull]
        public ICommandBase ReloadSessionCommand { get; }

        protected internal override IEnumerable<string> TextAssetTypes
        {
            get
            {
                yield return nameof(EffectShaderAsset);
                yield return nameof(EffectCompositorAsset);
            }
        }

        protected override void RestartAndCreateNewSession()
        {
            restartArguments = "/NewProject";
            CloseAndRestart();
        }

        protected override async Task RestartAndOpenSession(UFile sessionPath)
        {
            if (sessionPath != null && !File.Exists(sessionPath.ToOSPath()))
            {
                await ServiceProvider.Get<IDialogService>().MessageBoxAsync(Tr._p("Message", "The file {0} does not exist.").ToFormat(sessionPath.ToOSPath()));
                return;
            }
            if (sessionPath == null)
            {
                sessionPath = await EditorDialogHelper.BrowseForExistingProject(ServiceProvider);
            }

            // Operation cancelled
            if (sessionPath == null)
                return;

            restartArguments = $"\"{sessionPath.ToOSPath()}\"";
            await CloseAndRestart();
        }

        /// <inheritdoc/>
        public override void Destroy()
        {
            Preview?.Destroy();
            base.Destroy();
        }

        /// <summary>
        /// Attempts to close the window and then restarts if closing succeeded.
        /// </summary>
        [NotNull]
        private Task CloseAndRestart()
        {
            return ServiceProvider.Get<IDialogService2>().CloseMainWindow(RestartOnClosed);
        }

        private void OpenAboutPage()
        {
            ServiceProvider.Get<IStrideDialogService>().ShowAboutPage();
        }

        private void RestartOnClosed()
        {
            try
            {
                var process = new Process
                {
                    StartInfo =
                    {
                        // Make sure to use .exe rather than .dll (.NET Core)
                        FileName = LoaderToolLocator.GetExecutable(Assembly.GetExecutingAssembly().Location),
                        Arguments = restartArguments,
                    }
                };
                process.Start();
            }
            catch (Exception e)
            {
                e.Ignore();
            }
        }
    }
}

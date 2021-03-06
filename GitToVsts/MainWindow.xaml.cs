﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using EvilBaschdi.Core.Application;
using EvilBaschdi.Core.Browsers;
using EvilBaschdi.Core.Wpf;
using GitToVsts.Core;
using GitToVsts.Internal.Git;
using GitToVsts.Internal.TeamServices;
using GitToVsts.Model;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace GitToVsts
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    // ReSharper disable once RedundantExtendsListEntry
    public partial class MainWindow : MetroWindow
    {
        /// <summary>
        ///     ObservableColletion to contain GitRepositories.
        /// </summary>
        public ObservableCollection<GitRepositoryObservableCollectionItem> GitRepositoryObservableCollection { get; set; }

        readonly ObservableCollection<GitRepositoryObservableCollectionItem> _migrationFailedRepos = new ObservableCollection<GitRepositoryObservableCollectionItem>();
        readonly ObservableCollection<GitRepositoryObservableCollectionItem> _migrationSuccessRepos = new ObservableCollection<GitRepositoryObservableCollectionItem>();

        //private read-only BackgroundWorker _bw;
        private IGitRepositories _gitRepositories;

        private readonly IMetroStyle _style;
        private readonly IApplicationSettings _applicationSettings;
        private IProjects _projects;
        private ITemplates _templates;
        private readonly IDialogService _dialogService;
        private int _overrideProtection;
        private Configuration _configuration;
        private KeyValuePair<string, string> _result;
        private ProgressDialogController _controller;

        /// <summary>
        ///     MainWindow
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            ISettings coreSettings = new CoreSettings(Properties.Settings.Default);
            _applicationSettings = new ApplicationSettings();
            IThemeManagerHelper themeManagerHelper = new ThemeManagerHelper();
            _style = new MetroStyle(this, Accent, ThemeSwitch, coreSettings, themeManagerHelper);
            _style.Load(true);
            _dialogService = new DialogService(this);
            var linkerTime = Assembly.GetExecutingAssembly().GetLinkerTime();
            LinkerTime.Content = linkerTime.ToString(CultureInfo.InvariantCulture);
            Load();
        }

        #region GitTab

        //todo: auslagern
        private void LoadGitRepositoryList()
        {
            GitRepositoryObservableCollection = GetGitRepositoryObservableCollection();
            GitRepositoryObservableCollectionBox.ItemsSource = GitRepositoryObservableCollection;
            GitRepositoryMigrationFailedObservableCollectionBox.ItemsSource = _migrationFailedRepos;
            GitRepositoryMigrationSuccessObservableCollectionBox.ItemsSource = _migrationSuccessRepos;
        }

        private ObservableCollection<GitRepositoryObservableCollectionItem> GetGitRepositoryObservableCollection()
        {
            var collection = new ObservableCollection<GitRepositoryObservableCollectionItem>();

            int i = 1;
            foreach (var repository in _gitRepositories.Value)
            {
                collection.Add(new GitRepositoryObservableCollectionItem
                               {
                                   Displayname = $"{i++}_{repository.Name}",
                                   Repository = repository
                               });
            }

            return collection;
        }

        private void GitLoginOnClick(object sender, RoutedEventArgs e)
        {
            _applicationSettings.GitUser = GitUsername.Text;
            _applicationSettings.GitPassword = GitPassword.Password;

            var getGitUser = new GetGitUser(_applicationSettings);
            if (getGitUser.Value != null)
            {
                _applicationSettings.GitSource = GitSource.Text;
                _gitRepositories = new GetGitRepositories(_applicationSettings);
                LoadGitRepositoryList();
                var convertGitAvatar = new ConvertGitAvatart();
                GitAvatar.Source = convertGitAvatar.ValueFor(getGitUser.Value);
                GitAvatar.Visibility = Visibility.Visible;
                GitLogin.Visibility = Visibility.Hidden;

                _dialogService.ShowMessage("Successful", $"'{getGitUser.Value.Login}' was successfully authenticated {Environment.NewLine}Please switch to 'Repositories'");
                RepoTab.IsEnabled = true;
            }
            else
            {
                _applicationSettings.GitUser = "";
                _applicationSettings.GitPassword = "";
            }
        }


        private void SourceType(object sender, EventArgs e)
        {
            if (_overrideProtection == 0)
            {
                return;
            }
            var toggleSwitch = (ToggleSwitch) sender;
            _applicationSettings.GitSourceType = toggleSwitch.IsChecked.HasValue && toggleSwitch.IsChecked.Value ? "orgs" : "users";
        }

        private void ValidateGitTextBoxesOnTextChanged(object sender, EventArgs e)
        {
            GitLogin.IsEnabled = !string.IsNullOrWhiteSpace(GitUsername.Text) &&
                                 !string.IsNullOrWhiteSpace(GitPassword.Password) &&
                                 !string.IsNullOrWhiteSpace(GitSource.Text);
        }

        #endregion GitTab

        #region RepoTab

        private void MigrateToVsTsOnClick(object sender, RoutedEventArgs e)
        {
            SetRepoLabelContent();
        }

        private void MigrateAllOnClick(object sender, RoutedEventArgs e)
        {
            foreach (var gitRepositoryObservableCollectionItem in GitRepositoryObservableCollection)
            {
                gitRepositoryObservableCollectionItem.MigrateToVsTs = true;
            }
            GitRepositoryObservableCollectionBox.ItemsSource = null;
            GitRepositoryObservableCollectionBox.ItemsSource = GitRepositoryObservableCollection;

            SetRepoLabelContent();
        }

        private void SetRepoLabelContent()
        {
            var checkedItemsCount = GitRepositoryObservableCollection.Count(attribute => attribute.MigrateToVsTs);
            RepoLabel.Content = $"Repositories chosen to migrate: {checkedItemsCount}";
            VsTab.IsEnabled = checkedItemsCount != 0;
        }

        #endregion RepoTab

        #region VsTsTab

        private void VsLoginOnClick(object sender, RoutedEventArgs e)
        {
            _applicationSettings.VsUser = VsUsername.Text;
            _applicationSettings.VsPassword = VsPassword.Password;
            _applicationSettings.VsSource = VsSource.Text;
            _applicationSettings.VsProject = VsProjects.Text;

            VsProjects.IsEnabled = true;
            VsTemplates.IsEnabled = true;
            MigrationFailedTab.IsEnabled = true;
            SuccessfulTab.IsEnabled = true;

            _projects = new GetProjects(_applicationSettings);
            foreach (var project in _projects.Value.Value)
            {
                VsProjects.Items.Add(project.Name);
            }

            _templates = new GetTemplates(_applicationSettings);
            foreach (var template in _templates.Value.Value)
            {
                VsTemplates.Items.Add(template.Name);
            }
            VsLogin.IsEnabled = false;
        }

        private void VsTemplatesOnDropDownClosed(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(VsTemplates.Text) && !string.IsNullOrWhiteSpace(VsTemplates.Text))
            {
                RunTab.IsEnabled = true;
            }
        }

        private void VsProjectsOnDropDownClosed(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(VsTemplates.Text) && !string.IsNullOrWhiteSpace(VsTemplates.Text))
            {
                RunTab.IsEnabled = true;
            }
        }

        private void ValidateVsTextBoxesOnTextChanged(object sender, RoutedEventArgs e)
        {
            VsLogin.IsEnabled = !string.IsNullOrWhiteSpace(VsUsername.Text) &&
                                !string.IsNullOrWhiteSpace(VsPassword.Password) &&
                                !string.IsNullOrWhiteSpace(VsSource.Text);
        }

        #endregion VsTsTab

        #region RunTab

        private async void RunMigrationOnClickAsync(object sender, RoutedEventArgs e)
        {
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
            await RunAsync();
        }

        private async Task RunAsync()
        {
            var configuration = new Configuration
                                {
                                    VsTemplate = VsTemplates.Text,
                                    VsProject = VsProjects.Text
                                };
            Cursor = Cursors.Wait;
            _configuration = configuration;

            var options = new MetroDialogSettings
                          {
                              ColorScheme = MetroDialogColorScheme.Theme
                          };

            MetroDialogOptions = options;
            _controller = await this.ShowProgressAsync("Please wait...", "Repositories are getting migrated.", true, options);
            _controller.SetIndeterminate();
            _controller.Canceled += ControllerCanceled;
            await RunRepositoryMigrationAsync();
            TaskCompleted();
        }


        /// <summary>
        /// </summary>
        /// <returns></returns>
        //todo: CS1998 evaluate
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task RunRepositoryMigrationAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var repoPaths = new ConcurrentBag<string>();
            var configuration = _configuration;
            var checkedItems = GitRepositoryObservableCollection.Where(attribute => attribute.MigrateToVsTs);
            var gitCommands = new GitCommands();
            var migrate = new MigrateRepository(_applicationSettings, _templates, _projects, gitCommands, configuration.VsTemplate, configuration.VsProject);

            var repositoriesToMigrate = checkedItems as IList<GitRepositoryObservableCollectionItem> ?? checkedItems.ToList();
            try
            {
                Parallel.ForEach(repositoriesToMigrate, checkedItem => checkedItem.MigrationSuccessful = false);

                repositoriesToMigrate.Take(5)
                                     .ToList()
                                     .ForEach(checkedItem =>
                                              {
                                                  var response = migrate.ValueFor(checkedItem.Repository);
                                                  if (response.Code != 200)
                                                  {
                                                      File.AppendAllText("C:/temp/GitVSTSMigration.txt", $@"{response.Value}{Environment.NewLine}");
                                                  }
                                                  else
                                                  {
                                                      checkedItem.MigrationSuccessful = true;
                                                      repoPaths.Add(response.Value);
                                                  }
                                              });
                var restRepositoriesToMigrate = repositoriesToMigrate.Skip(5).ToList();
                Parallel.ForEach(restRepositoriesToMigrate, checkedItem =>
                                                            {
                                                                var response = migrate.ValueFor(checkedItem.Repository);
                                                                if (response.Code != 200)
                                                                {
                                                                    File.AppendAllText("C:/temp/GitVSTSMigration.txt", $@"{response.Value}{Environment.NewLine}");
                                                                }
                                                                else
                                                                {
                                                                    checkedItem.MigrationSuccessful = true;
                                                                    repoPaths.Add(response.Value);
                                                                }
                                                            });
            }
            catch (Exception ex)
            {
                File.AppendAllText("C:/temp/GitVSTSMigration.txt", $@"{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
            finally
            {
                if (_applicationSettings.DeleteTempRepos)
                {
                    foreach (var repoPath in repoPaths)
                    {
                        if (Directory.Exists(repoPath))
                        {
                            try
                            {
                                Directory.Delete(repoPath);
                            }
                            catch (Exception ex)
                            {
                                File.AppendAllText("C:/temp/GitVSTSMigration.txt", $@"{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.StackTrace}");
                            }
                        }
                    }
                }

                _result = new KeyValuePair<string, string>("Finished", $"All {repositoriesToMigrate.Count} repositories were migrated.");
            }
        }

        /// <summary>
        /// </summary>
        public void RefreshMigrationRepos()
        {
            _migrationFailedRepos.Clear();
            _migrationSuccessRepos.Clear();

            GitRepositoryObservableCollection.Where(attribute => attribute.MigrateToVsTs && !attribute.MigrationSuccessful)
                                             .ToList()
                                             .ForEach(repo => _migrationFailedRepos.Add(repo));

            GitRepositoryObservableCollection.Where(attribute => attribute.MigrateToVsTs && attribute.MigrationSuccessful)
                                             .ToList()
                                             .ForEach(repo => _migrationSuccessRepos.Add(repo));
        }


        private void TaskCompleted()
        {
            _controller.CloseAsync();
            _controller.Closed += ControllerClosed;
        }

        private void ControllerClosed(object sender, EventArgs e)
        {
            _dialogService.ShowMessage(_result.Key, _result.Value);
            RefreshMigrationRepos();
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            TaskbarItemInfo.ProgressValue = 1;
            Cursor = Cursors.Arrow;
        }

        private void ControllerCanceled(object sender, EventArgs e)
        {
            _controller.CloseAsync();
            _controller.Closed += ControllerClosed;
        }

        #endregion RunTab

        #region Window Methods

        private void Load()
        {
            GitUsername.Text = _applicationSettings.GitUser;
            GitPassword.Password = _applicationSettings.GitPassword;
            GitSource.Text = _applicationSettings.GitSource;
            VsUsername.Text = _applicationSettings.VsUser;
            VsPassword.Password = _applicationSettings.VsPassword;
            VsSource.Text = _applicationSettings.VsSource;
            VsProjects.Text = _applicationSettings.VsProject;
            LoggingPath.Text = _applicationSettings.LoggingPath;
            TempPath.Text = _applicationSettings.TempPath;
            GitBinPath.Text = _applicationSettings.GitBinPath;
            CleanUpSwitch.IsChecked = _applicationSettings.DeleteTempRepos;

            switch (_applicationSettings.GitSourceType)
            {
                case "users":
                    SourceSwitch.IsChecked = false;
                    break;

                case "orgs":
                    SourceSwitch.IsChecked = true;
                    break;
            }

            _overrideProtection = 1;
        }

        private void BrowseLoggingPathClick(object sender, RoutedEventArgs e)
        {
            var browser = new ExplorerFolderBrowser
                          {
                              SelectedPath = _applicationSettings.LoggingPath
                          };
            browser.ShowDialog();
            _applicationSettings.LoggingPath = browser.SelectedPath;
            LoggingPath.Text = _applicationSettings.LoggingPath;
        }

        private void LoggingPathOnLostFocus(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(LoggingPath.Text))
            {
                _applicationSettings.LoggingPath = LoggingPath.Text;
            }
        }

        private void BrowseTempPathClick(object sender, RoutedEventArgs e)
        {
            var browser = new ExplorerFolderBrowser
                          {
                              SelectedPath = _applicationSettings.TempPath
                          };
            browser.ShowDialog();
            _applicationSettings.TempPath = browser.SelectedPath;
            TempPath.Text = _applicationSettings.TempPath;
        }

        private void TempPathOnLostFocus(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(TempPath.Text))
            {
                _applicationSettings.TempPath = TempPath.Text;
            }
        }

        private void CleanUp(object sender, EventArgs e)
        {
            var toggleSwitch = (ToggleSwitch) sender;
            _applicationSettings.DeleteTempRepos = toggleSwitch.IsChecked.HasValue && toggleSwitch.IsChecked.Value;
        }

        private void BrowseGitPathClick(object sender, RoutedEventArgs e)
        {
            var browser = new ExplorerFolderBrowser
                          {
                              SelectedPath = _applicationSettings.GitBinPath
                          };
            browser.ShowDialog();
            if (File.Exists($@"{browser.SelectedPath}\git.exe"))
            {
                _applicationSettings.GitBinPath = browser.SelectedPath;
                GitBinPath.Text = _applicationSettings.GitBinPath;
            }
            else
            {
                _dialogService.ShowMessage("Path Error", "Path does not contain a 'git.exe'");
            }
        }

        private void GitPathOnLostFocus(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(GitBinPath.Text) && File.Exists($@"{GitBinPath.Text}\git.exe"))
            {
                _applicationSettings.GitBinPath = GitBinPath.Text;
            }
            else
            {
                _dialogService.ShowMessage("Path Error", "Path does not contain a 'git.exe'");
            }
        }

        #endregion Window Methods

        #region Fly-out

        private void ToggleSettingsFlyoutClick(object sender, RoutedEventArgs e)
        {
            ToggleFlyout(0);
        }

        private void ToggleFlyout(int index, bool stayOpen = false)
        {
            var activeFlyout = (Flyout) Flyouts.Items[index];
            if (activeFlyout == null)
            {
                return;
            }

            foreach (
                var nonactiveFlyout in
                Flyouts.Items.Cast<Flyout>()
                       .Where(nonactiveFlyout => nonactiveFlyout.IsOpen && nonactiveFlyout.Name != activeFlyout.Name))
            {
                nonactiveFlyout.IsOpen = false;
            }

            activeFlyout.IsOpen = activeFlyout.IsOpen && stayOpen || !activeFlyout.IsOpen;
        }

        #endregion Fly-out

        #region MetroStyle

        private void SaveStyleClick(object sender, RoutedEventArgs e)
        {
            if (_overrideProtection == 0)
            {
                return;
            }
            _style.SaveStyle();
        }

        private void Theme(object sender, EventArgs e)
        {
            if (_overrideProtection == 0)
            {
                return;
            }
            var routedEventArgs = e as RoutedEventArgs;
            if (routedEventArgs != null)
            {
                _style.SetTheme(sender, routedEventArgs);
            }
            else
            {
                _style.SetTheme(sender);
            }
        }

        private void AccentOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overrideProtection == 0)
            {
                return;
            }
            _style.SetAccent(sender, e);
        }

        #endregion MetroStyle
    }
}
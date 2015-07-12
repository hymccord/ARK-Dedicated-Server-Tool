﻿using ARK_Server_Manager.Lib;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ARK_Server_Manager
{
    /// <summary>
    /// Interaction logic for ServerSettings.xaml
    /// </summary>
    partial class ServerSettingsControl : UserControl
    {
        public static readonly DependencyProperty SettingsProperty =
            DependencyProperty.Register("Settings", typeof(ServerProfile), typeof(ServerSettingsControl));
        public static readonly DependencyProperty RuntimeProperty = 
            DependencyProperty.Register("Runtime", typeof(ServerRuntime), typeof(ServerSettingsControl));
        public static readonly DependencyProperty NetworkInterfacesProperty = 
            DependencyProperty.Register("NetworkInterfaces", typeof(List<NetworkAdapterEntry>), typeof(ServerSettingsControl), new PropertyMetadata(new List<NetworkAdapterEntry>()));
        public static readonly DependencyProperty ServerProperty =
            DependencyProperty.Register("Server", typeof(Server), typeof(ServerSettingsControl), new PropertyMetadata(null, ServerPropertyChanged));

        CancellationTokenSource upgradeCancellationSource;
        RCONWindow rconWindow;

        public ServerManager ServerManager
        {
            get { return (ServerManager)GetValue(ServerManagerProperty); }
            set { SetValue(ServerManagerProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ServerManager.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ServerManagerProperty =
            DependencyProperty.Register("ServerManager", typeof(ServerManager), typeof(ServerSettingsControl), new PropertyMetadata(null));

        
        public Server Server
        {
            get { return (Server)GetValue(ServerProperty); }
            set { SetValue(ServerProperty, value); }
        }

        private static void ServerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ssc = (ServerSettingsControl)d;
            var server = (Server)e.NewValue;
            ssc.Settings = server.Profile;
            ssc.Runtime = server.Runtime;
            ssc.ReinitializeNetworkAdapters();
        }
        
        public ServerProfile Settings
        {
            get { return GetValue(SettingsProperty) as ServerProfile; }
            set { SetValue(SettingsProperty, value); }
        }

        public ServerRuntime Runtime
        {
            get { return GetValue(RuntimeProperty) as ServerRuntime; }
            set { SetValue(RuntimeProperty, value); }
        }

        public List<NetworkAdapterEntry> NetworkInterfaces
        {
            get { return (List<NetworkAdapterEntry>)GetValue(NetworkInterfacesProperty); }
            set { SetValue(NetworkInterfacesProperty, value); }
        }

        public ServerSettingsControl()
        {
            InitializeComponent();
            this.ServerManager = ServerManager.Instance;           
        }

        private void ReinitializeNetworkAdapters()
        {
            var adapters = NetworkUtils.GetAvailableIPV4NetworkAdapters();

            //
            // Filter out self-assigned addresses
            //
            adapters.RemoveAll(a => a.IPAddress.StartsWith("169.254."));           
            this.NetworkInterfaces = adapters;            


            //
            // If there isn't already an adapter assigned, pick one
            //
            var preferredIP = NetworkUtils.GetPreferredIP(adapters);
            preferredIP.Description = "(Recommended) " + preferredIP.Description;
            if(String.IsNullOrWhiteSpace(this.Settings.ServerIP))
            {
                if(preferredIP != null)
                {
                    this.Settings.ServerIP = preferredIP.IPAddress;
                }
            } 
            else if(adapters.FirstOrDefault(a => String.Equals(a.IPAddress, this.Settings.ServerIP, StringComparison.OrdinalIgnoreCase)) == null) 
            {
                MessageBox.Show(
                    String.Format("Your Local IP address {0} is no longer available.  Please review the available IP addresses and select a valid one.  If you have a server running on the original IP, you will need to stop it first.", this.Settings.ServerIP), 
                    "Local IP invalid", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);            
            }
        }        

        private async void Upgrade_Click(object sender, RoutedEventArgs e)
        {
            if(this.Runtime.Status == ServerRuntime.ServerStatus.Updating)
            {
                // Cancel the current upgrade
                upgradeCancellationSource.Cancel();
            }
            else
            {
                if(this.Runtime.IsRunning)
                {
                    var result = MessageBox.Show("The server must be stopped to upgrade.  Do you wish to proceed?", "Server running", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if(result == MessageBoxResult.No)
                    {
                        return;
                    }
                }

                // Start the upgrade
                upgradeCancellationSource = new CancellationTokenSource();
                await this.Server.UpgradeAsync(upgradeCancellationSource.Token, validate: true);
            }                       
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (this.Runtime.IsRunning)
            {
                var result = MessageBox.Show("This will shut down the server.  Do you wish to proceed?", "Stop the server?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }

                await this.Server.StopAsync();
            }
            else
            {
                this.Settings.Save();
                await this.Server.StartAsync();
            }
        }

        private void SelectInstallDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            dialog.Title = "Select Install Directory";
            if(!String.IsNullOrWhiteSpace(Settings.InstallDirectory))
            {
                dialog.InitialDirectory = Settings.InstallDirectory;
            }

            var result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                Settings.InstallDirectory = dialog.FileName;
            }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.EnsureFileExists = true;
            dialog.Multiselect = false;
            dialog.Title = "Load Server Profile or GameUserSettings.ini";
            dialog.Filters.Add(new CommonFileDialogFilter("Profile", Config.Default.LoadProfileExtensionList));
            if(!Directory.Exists(Config.Default.ConfigDirectory))
            {
                System.IO.Directory.CreateDirectory(Config.Default.ConfigDirectory);
            }

            dialog.InitialDirectory = Config.Default.ConfigDirectory;
            var result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                try
                {
                    this.Server.ImportFromPath(dialog.FileName);
                    this.Settings = this.Server.Profile;
                    this.Runtime = this.Server.Runtime;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(String.Format("The profile at {0} failed to load.  The error was: {1}\r\n{2}", dialog.FileName, ex.Message, ex.StackTrace), "Profile failed to load", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }            
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.Save();
        }

        private void CopyProfile_Click(object sender, RoutedEventArgs e)
        {
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
        }

        private void ShowCmd_Click(object sender, RoutedEventArgs e)
        {
            var cmdLine = new CommandLine(String.Format("{0} {1}", this.Runtime.GetServerExe(), this.Settings.GetServerArgs()));
            cmdLine.ShowDialog();
        }

        private void RemovePlayerLevel_Click(object sender, RoutedEventArgs e)
        {            
            if(this.Settings.PlayerLevels.Count == 1)
            {
                MessageBox.Show("You can't delete the last level.  If you want to disable the feature, uncheck Enable Custom Level Progressions.", "Can't delete last item", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
            else
            {
                var level = ((Level)((Button)e.Source).DataContext);
                this.Settings.PlayerLevels.RemoveLevel(level);            
            }
        }

        private void AddPlayerLevel_Click(object sender, RoutedEventArgs e)
        {
            var level = ((Level)((Button)e.Source).DataContext);
            this.Settings.PlayerLevels.AddNewLevel(level);
        }

        private void RemoveDinoLevel_Click(object sender, RoutedEventArgs e)
        {
            if (this.Settings.DinoLevels.Count == 1)
            {
                MessageBox.Show("You can't delete the last level.  If you want to disable the feature, uncheck Enable Custom Level Progressions.", "Can't delete last item", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
            else
            {
                var level = ((Level)((Button)e.Source).DataContext);
                this.Settings.DinoLevels.RemoveLevel(level);
            }
        }

        private void AddDinoLevel_Click(object sender, RoutedEventArgs e)
        {
            var level = ((Level)((Button)e.Source).DataContext);
            this.Settings.DinoLevels.AddNewLevel(level);
        }

        private void PlayerLevels_Recalculate(object sender, RoutedEventArgs e)
        {
            this.Settings.PlayerLevels.UpdateTotals();
            this.CustomPlayerLevelsView.Items.Refresh();
        }

        private void DinoLevels_Recalculate(object sender, RoutedEventArgs e)
        {
            this.Settings.DinoLevels.UpdateTotals();
            this.CustomDinoLevelsView.Items.Refresh();
        }

        private void RefreshLocalIPs_Click(object sender, RoutedEventArgs e)
        {
            ReinitializeNetworkAdapters();
        }

        private void DinoLevels_Clear(object sender, RoutedEventArgs e)
        {
            this.Settings.ClearLevelProgression(ServerProfile.LevelProgression.Dino);
        }

        private void DinoLevels_Reset(object sender, RoutedEventArgs e)
        {
            this.Settings.ResetLevelProgressionToDefault(ServerProfile.LevelProgression.Dino);
        }

        private void PlayerLevels_Clear(object sender, RoutedEventArgs e)
        {
            this.Settings.ClearLevelProgression(ServerProfile.LevelProgression.Player);
        }

        private void PlayerLevels_Reset(object sender, RoutedEventArgs e)
        {
            this.Settings.ResetLevelProgressionToDefault(ServerProfile.LevelProgression.Player);
        }

        private void DinoSpawn_Reset(object sender, RoutedEventArgs e)
        {
            this.Settings.ResetDinoSpawnsToDefault();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            await ServerManager.Instance.CheckForUpdatesAsync();
        }

        private void OpenRCON_Click(object sender, RoutedEventArgs e)
        {
            if(this.rconWindow == null || !this.rconWindow.IsLoaded)
            {
                this.rconWindow = new RCONWindow(this.Settings.ProfileName, new IPEndPoint(IPAddress.Parse(this.Settings.ServerIP), this.Settings.RCONPort), this.Settings.AdminPassword);                
            }

            this.rconWindow.Show();
            this.rconWindow.Focus();
        }
    }
}

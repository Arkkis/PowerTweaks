using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Windows;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Windows.Controls;
using System.Linq;

namespace PowerTweaks
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string _registryDirectory = @"SOFTWARE\Classes\Directory\";
        private readonly List<ContextItem> _items = new();
        private int _itemIndex = 0;
        private readonly int _buttonHeight = 25;
        private readonly int _buttonMargin = 10;

        //bool isAdmin = false;

        public MainWindow()
        {
            InitializeComponent();

            _items = GetItems();

            foreach (var item in _items)
            {
                AddItemToGrid(item, Button_Click_AddContext);
            }

            AddItemToGrid(new ContextItem { KeyPath = "Add all to context menu" }, new RoutedEventHandler((s, e) =>
            {
                AddAllMenus();
            }));

            AddItemToGrid(new ContextItem { KeyPath = "Remove all from context menu" }, new RoutedEventHandler((s, e) =>
            {
                RemoveAllChanges();
            }));

            AddItemToGrid(new ContextItem { KeyPath = "Exit" }, new RoutedEventHandler((s, e) =>
            {
                AppWindow.Close();
            }));

            if (!IsAdmin())
            {
                MessageBox.Show("not admin");
                //IsAdminText.Visibility = Visibility.Visible;
            }
        }

        private void AddItemToGrid(ContextItem itemToAdd, RoutedEventHandler clickEvent)
        {
            if (itemToAdd.Separator)
            {
                AddSeparatorToGrid();
                return;
            }

            AppWindow.Height += _buttonHeight + _buttonMargin;

            ButtonGrid.RowDefinitions.Add(CreateNewDefinition());

            var button = new Button
            {
                Height = _buttonHeight,
                Margin = new Thickness { Bottom = _buttonMargin },
                Content = itemToAdd.KeyPath
            };

            button.Click += clickEvent;
            ButtonGrid.Children.Add(button);
            Grid.SetRow(button, _itemIndex);
            _itemIndex++;
        }

        private void AddSeparatorToGrid()
        {
            ButtonGrid.RowDefinitions.Add(CreateNewDefinition());
            var separator = new Separator();
            ButtonGrid.Children.Add(separator);
            Grid.SetRow(separator, _itemIndex);
            _itemIndex++;
        }

        private RowDefinition CreateNewDefinition()
        {
            return new RowDefinition { Height = new GridLength(_buttonHeight + _buttonMargin) };
        }

        private void Button_Click_AddContext(object sender, RoutedEventArgs e)
        {
            var content = (sender as Button).Content.ToString();
            var menuItems = JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));

            AddItemToContextMenu(menuItems.FirstOrDefault(item => item.KeyPath == content));
        }

        private static void AddAllMenus()
        {
            var menuItems = JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));

            foreach (var item in menuItems)
            {
                AddItemToContextMenu(item);
            }
        }

        private static List<ContextItem> GetItems()
        {
            return JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));
        }

        public class ContextItem
        {
            public string KeyPath { get; set; }
            public bool IsAdmin { get; set; }
            public PromptType PromptType { get; set; }
            public bool Background { get; set; }
            public bool Separator { get; set; }
        }

        public enum PromptType
        {
            CommandPrompt = 0,
            PowerShell = 1,
            CommandPromptCommand = 2
        }

        private static void AddItemToContextMenu(ContextItem item)
        {
            _ = item ?? throw new ArgumentNullException(nameof(item));

            if (string.IsNullOrEmpty(item.KeyPath))
            {
                return;
            }

            var promptPath = Environment.SystemDirectory;
            string value, backgroundValue, baseString;

            static string ReplaceInString(string baseString, string variable)
            {
                return baseString.Replace("%replace%", variable);
            }

            switch (item.PromptType)
            {
                case PromptType.CommandPrompt:
                    promptPath += @"\cmd.exe";

                    if (item.IsAdmin)
                    {
                        baseString = "PowerShell.exe -windowstyle hidden -Command \"Start-Process cmd.exe -Verb RunAs -ArgumentList @('/s,/k,pushd,%replace%')";
                        break;
                    }

                    baseString = "cmd.exe /K \"cd %replace%\"";
                    break;

                case PromptType.PowerShell:
                    promptPath += @"\WindowsPowerShell\v1.0\powershell.exe";

                    if (item.IsAdmin)
                    {
                        baseString = "PowerShell.exe -Command \"Start-Process PowerShell -Verb RunAs -ArgumentList \"\"\"-NoExit -c Set-Location '%replace%'\"\"\"";
                        break;
                    }

                    baseString = "PowerShell.exe -NoExit -Command \"Set-Location '%replace%'\"";
                    break;

                case PromptType.CommandPromptCommand:
                    promptPath += @"\cmd.exe";
                    baseString = "cmd.exe /K \"rmdir / s / q \"%1\"\" && exit";
                    break;

                default:
                    return;
            }

            value = ReplaceInString(baseString, "%1");
            backgroundValue = ReplaceInString(baseString, "%W");

            var path = _registryDirectory;
            var pathNormal = path + "shell\\" + item.KeyPath;
            var pathNormalCommand = pathNormal + "\\command";
            var pathBackground = path + "Background\\shell\\" + item.KeyPath;
            var pathBackgroundCommand = pathBackground + "\\command";

            File.AppendAllText("log.txt", pathNormalCommand + Environment.NewLine + value + Environment.NewLine);
            File.AppendAllText("log.txt", pathBackgroundCommand + Environment.NewLine + backgroundValue + Environment.NewLine + Environment.NewLine);

            if (RegValueExists(pathNormal, "Icon"))
            {
                return;
            }

            if (RegValueExists(pathBackground, "Icon"))
            {
                return;
            }

            var failed = new List<bool>
            {
                AddItemToRegistry(pathNormal, promptPath, true),
                AddItemToRegistry(pathNormalCommand, value),
                
            };

            if (item.Background)
            {
                failed.Add(AddItemToRegistry(pathBackground, promptPath, true));
                failed.Add(AddItemToRegistry(pathBackgroundCommand, backgroundValue));
            }

            if (failed.Contains(false))
            {
                MessageBox.Show("something went wronk");
            }
        }

        private static bool AddItemToRegistry(string path, string command, bool withIcon = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            if (string.IsNullOrEmpty(command))
            {
                return true;
            }

            if (!RegKeyExists(path))
            {
                Debug.WriteLine(path);
                Registry.CurrentUser.CreateSubKey(path);

                if (withIcon)
                {
                    Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree).SetValue("Icon", command + ",0");
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree).SetValue("", command);
                }

                if (!RegKeyExists(path))
                {
                    return false;
                }

                return true;
            }

            return true;
        }

        private void RemoveAllChanges()
        {
            // Remove our keys
            foreach (var item in _items.Where(item => !string.IsNullOrEmpty(item.KeyPath)))
            {
                var path = _registryDirectory;
                var pathNormal = path + "shell\\" + item.KeyPath;
                var pathBackground = path + "Background\\shell\\" + item.KeyPath;

                if (RegKeyExists(pathNormal))
                {
                    Debug.WriteLine(pathNormal);
                    Registry.CurrentUser.DeleteSubKeyTree(pathNormal);
                }

                if (RegKeyExists(pathBackground))
                {
                    Debug.WriteLine(pathBackground);
                    Registry.CurrentUser.DeleteSubKeyTree(pathBackground);
                }
            }
        }

        private static bool RegKeyExists(string path)
        {
            if (Registry.CurrentUser.OpenSubKey(path) == null)
            {
                return false;
            }

            return true;
        }

        private static bool RegValueExists(string path, string value)
        {
            if (RegKeyExists(path))
            {
                if (Registry.CurrentUser.OpenSubKey(path).GetValue(value) == null)
                {
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

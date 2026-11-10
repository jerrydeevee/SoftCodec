using System.ComponentModel;
using System.Windows;
using Forms=System.Windows.Forms;
namespace SoftCodec.App;
public partial class MainWindow : Window
{
 readonly MainViewModel model;readonly Forms.NotifyIcon tray;bool quitting;
 public MainWindow(MainViewModel viewModel)
 {
  InitializeComponent();DataContext=model=viewModel;tray=new(){Text="SoftCodec · Radio Aa en Hunze",Icon=System.Drawing.SystemIcons.Application,Visible=true};tray.DoubleClick+=(_,_)=>ShowWindow();var menu=new Forms.ContextMenuStrip();menu.Items.Add("Studio openen",null,(_,_)=>ShowWindow());menu.Items.Add("Afsluiten",null,async(_,_)=>await Quit());tray.ContextMenuStrip=menu;Closing+=OnClosing;
 }
 void ShowWindow(){Show();WindowState=WindowState.Normal;Activate();}
 void OnClosing(object? sender,CancelEventArgs e){if(!quitting){e.Cancel=true;Hide();}}
 async Task Quit(){if(model.Running&&System.Windows.MessageBox.Show("Ontvanger stoppen en SoftCodec afsluiten?","SoftCodec",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;quitting=true;tray.Visible=false;tray.Dispose();await ((App)System.Windows.Application.Current).ExitAsync();}
 async void SaveSettings_Click(object sender,RoutedEventArgs e){try{model.SetPasswords(DigestPassword.Password,RegisterPassword.Password);DigestPassword.Clear();RegisterPassword.Clear();await model.SaveCommand.ExecuteAsync(null);}catch(Exception ex){System.Windows.MessageBox.Show(ex.Message,"Instellingen");}}
}

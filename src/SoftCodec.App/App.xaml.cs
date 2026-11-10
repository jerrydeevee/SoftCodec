using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace SoftCodec.App;
public partial class App : System.Windows.Application
{
 IHost? applicationHost;int exiting;
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  DispatcherUnhandledException+=(s,args)=>{System.Windows.MessageBox.Show(args.Exception.Message,"SoftCodec",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;};
  try { applicationHost=Host.CreateDefaultBuilder().ConfigureServices(services=>{services.AddSingleton<MainViewModel>();services.AddSingleton<MainWindow>();}).Build();await applicationHost.StartAsync();var window=applicationHost.Services.GetRequiredService<MainWindow>();window.Show();await applicationHost.Services.GetRequiredService<MainViewModel>().AutoStartAsync();if(e.Args.Contains("--minimized"))window.Hide(); }
  catch(Exception ex){System.Windows.MessageBox.Show(ex.Message,"SoftCodec starten",MessageBoxButton.OK,MessageBoxImage.Error);await ExitAsync(-1);}
 }
 public async Task ExitAsync(int exitCode=0)
 {
  if(Interlocked.Exchange(ref exiting,1)!=0)return;
  if(applicationHost is not null){await applicationHost.StopAsync();if(applicationHost is IAsyncDisposable asyncHost)await asyncHost.DisposeAsync();else applicationHost.Dispose();}
  Shutdown(exitCode);
 }
}

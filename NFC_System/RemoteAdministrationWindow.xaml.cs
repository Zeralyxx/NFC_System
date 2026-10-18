using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NFC_System;

public sealed partial class RemoteAdministrationWindow : Window
{
    public RemoteAdministrationWindow()
    {
        InitializeComponent();
        AdministrationControl.OwnerWindow=this;
        ConnectionButton.Visibility=DatabaseService.ActiveConnection.Role==ServerConnectionRole.RemoteAdministration?Visibility.Visible:Visibility.Collapsed;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100,850));
        Closed+=(_,_)=>AdministrationControl.Close();
    }

    private async void ConnectionButton_Click(object sender,RoutedEventArgs e)
    {
        if(AdministrationControl.IsSignedIn)
        {
            await new ContentDialog { Title="Sign out first",Content="Sign out of remote administration before changing this device's connection.",
                CloseButtonText="OK",XamlRoot=Content.XamlRoot }.ShowAsync();return;
        }
        var control=new ServerConnectionControl();control.Load(DatabaseService.ActiveConnection);
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap };
        var content=new StackPanel { Spacing=12,MaxWidth=350 };content.Children.Add(control);content.Children.Add(error);
        var dialog=new ContentDialog { Title="Device Connection",Content=new ScrollViewer { Content=content,MaxHeight=400 },
            PrimaryButtonText="Save",CloseButtonText="Cancel",XamlRoot=Content.XamlRoot };
        bool saving=false;
        dialog.Closing+=(_,args)=>{if(saving)args.Cancel=true;else control.CancelDiscovery();};
        dialog.PrimaryButtonClick+=async(_,args)=>
        {
            args.Cancel=true;if(saving||control.IsSearching)return;
            var deferral=args.GetDeferral();saving=true;dialog.IsPrimaryButtonEnabled=false;control.IsEnabled=false;
            try
            {
                var configuration=await DatabaseService.ValidateClientConnectionAsync(control.Configuration);
                DatabaseService.SaveConnectionConfiguration(configuration,AppSession.LoginVersion,beforeLogin:true);
                args.Cancel=false;
            }
            catch(Exception ex){error.Text=ex.Message;}
            finally{saving=false;dialog.IsPrimaryButtonEnabled=true;control.IsEnabled=true;deferral.Complete();}
        };
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)
        {
            await new ContentDialog { Title="Connection saved",Content="Restart the application to use the selected device role.",CloseButtonText="OK",XamlRoot=Content.XamlRoot }.ShowAsync();
            Application.Current.Exit();
        }
        control.CancelDiscovery();
    }
}

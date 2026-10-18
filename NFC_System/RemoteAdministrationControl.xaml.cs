using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class RemoteAdministrationControl : UserControl
{
    private FirebaseCloudSession? _session;
    private RemoteCloudStore? _cloud;
    private RemoteChangeQueue? _queue;
    private RemoteServerKey? _serverKey;
    private IReadOnlyList<RemoteStudent> _students=[];
    private RemoteStudent? _editing;
    private string _photo="";
    private bool _busy,_closed,_approved,_photoChanged;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromSeconds(15) };
    internal Window? OwnerWindow { get; set; }
    internal bool IsSignedIn=>_session!=null;
    private bool IsServer=>DatabaseService.ActiveConnection.Role==ServerConnectionRole.MainServer;
    private string Site=>DatabaseService.ActiveConnection.ServerId;

    public RemoteAdministrationControl()
    {
        InitializeComponent();
        SiteIdBox.Text=Site;
        PageTitle.Text=IsServer?"Main Server Cloud Connection":"Remote Administration";
        HardwareService.OnUidScanned+=OnUid;
        _timer.Tick+=async(_,_)=>
        {
            if(IsServer){ServerStatusText.Text=RemoteAdministrationHost.Status;return;}
            if(_approved&&!_busy)await RunAsync(SynchronizeAsync);
        };
        try {ComPortBox.ItemsSource=SerialPort.GetPortNames().OrderBy(x=>x).ToArray();if(ComPortBox.Items.Count>0)ComPortBox.SelectedIndex=0;}
        catch(Exception ex){PhotoStatus.Text="NFC ports unavailable: "+ex.Message;}
        if(IsServer&&RemoteAdministrationHost.Running)
        {
            SignInPanel.Visibility=Visibility.Collapsed;ServerPanel.Visibility=Visibility.Visible;
            StatusBar.Title="Server cloud connection running";_timer.Start();
        }
    }

    private void SetStatus(string title,string message="",InfoBarSeverity severity=InfoBarSeverity.Informational)
    {if(_closed)return;StatusBar.Title=title;StatusBar.Message=message;StatusBar.Severity=severity;}

    private async Task RunAsync(Func<Task> action)
    {
        if(_busy||_closed)return;
        _busy=true;WorkingBar.Visibility=Visibility.Visible;SignInButton.IsEnabled=false;
        RemotePanel.IsEnabled=false;ServerPanel.IsEnabled=false;AccountCommands.IsEnabled=false;
        try{await action();}
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){}
        catch(Exception ex){SetStatus("Action not completed",ex.Message,InfoBarSeverity.Error);}
        finally
        {
            _busy=false;
            if(!_closed){WorkingBar.Visibility=Visibility.Collapsed;SignInButton.IsEnabled=true;RemotePanel.IsEnabled=true;ServerPanel.IsEnabled=true;AccountCommands.IsEnabled=true;}
        }
    }

    private async void SignIn_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(IsServer)RemoteAdministrationHost.RequireServerOperator();
        _session?.Dispose();_session=RemoteAdministrationHost.NewSession();
        string password=PasswordBox.Password;PasswordBox.Password="";
        try{await _session.SignInAsync(EmailBox.Text,password,_lifetime.Token);}
        catch{_session.Dispose();_session=null;throw;}
        finally{password="";}
        AccountUidBox.Text=_session.Uid;AccountUidBox.Visibility=Visibility.Visible;AccountCommands.Visibility=Visibility.Visible;
        SignInPanel.Visibility=Visibility.Collapsed;
        _cloud=RemoteAdministrationHost.Store(_session,Site);
        if(!_session.EmailVerified){SetStatus("Email verification required","Send a verification email, then sign out and sign in again.",InfoBarSeverity.Warning);return;}
        if(IsServer)
        {
            await RemoteAdministrationHost.StartAsync(_session,_lifetime.Token);
            ServerPanel.Visibility=Visibility.Visible;SetStatus("Server cloud connection started");_timer.Start();
            MembersList.ItemsSource=await RemoteAdministrationHost.MembersAsync(_lifetime.Token);
        }
        else
        {
            await _cloud.RequireMemberAsync("administrator",_lifetime.Token);
            _approved=true;_queue=new(RemoteAdministrationHost.StorageDirectory,Site,_session.Uid);
            RemotePanel.Visibility=Visibility.Visible;
            await SynchronizeAsync();_timer.Start();
        }
    });

    private async void VerifyEmail_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(_session==null)throw new InvalidOperationException("Sign in first.");
        await _session.SendVerificationAsync(_lifetime.Token);SetStatus("Verification email sent","Open the email link, then sign out and sign in again.");
    });

    private async void SignOut_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(IsServer&&RemoteAdministrationHost.Running)
        {RemoteAdministrationHost.RequireServerOperator();await RemoteAdministrationHost.StopAsync();}
        SignOut();
    });

    private void SignOut()
    {
        _timer.Stop();StopScanning();_session?.Dispose();_session=null;_cloud=null;_queue=null;_serverKey=null;_approved=false;
        _students=[];StudentsList.ItemsSource=null;RequestsList.ItemsSource=null;
        ClearEditor();AccountUidBox.Text="";AccountUidBox.Visibility=Visibility.Collapsed;
        AccountCommands.Visibility=Visibility.Collapsed;RemotePanel.Visibility=Visibility.Collapsed;ServerPanel.Visibility=Visibility.Collapsed;
        SignInPanel.Visibility=Visibility.Visible;SetStatus("Signed out","Encrypted queued changes remain stored for this account.");
    }

    private async void StopServer_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {RemoteAdministrationHost.RequireServerOperator();await RemoteAdministrationHost.StopAsync();SignOut();});

    private async void SaveMember_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        await RemoteAdministrationHost.ApproveAsync(new(MemberUidBox.Text.Trim(),MemberEmailBox.Text.Trim(),MemberNameBox.Text.Trim(),
            "administrator",MemberStateBox.SelectedIndex==0),_lifetime.Token);
        SetStatus("Account access saved",severity:InfoBarSeverity.Success);
        MembersList.ItemsSource=await RemoteAdministrationHost.MembersAsync(_lifetime.Token);
    });

    private async void RefreshMembers_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {MembersList.ItemsSource=await RemoteAdministrationHost.MembersAsync(_lifetime.Token);});

    private void Member_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(MembersList.SelectedItem is not RemoteMember member || member.Role!="administrator")return;
        MemberUidBox.Text=member.Uid;MemberEmailBox.Text=member.Email;MemberNameBox.Text=member.Name;MemberStateBox.SelectedIndex=member.Active?0:1;
    }

    private async void Synchronize_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(IsServer){ServerStatusText.Text=RemoteAdministrationHost.Status;return;}
        if(!_approved)
        {
            await (_cloud??throw new InvalidOperationException("Sign in first.")).RequireMemberAsync("administrator",_lifetime.Token);
            _approved=true;_queue=new(RemoteAdministrationHost.StorageDirectory,Site,_session!.Uid);RemotePanel.Visibility=Visibility.Visible;
        }
        await SynchronizeAsync();_timer.Start();
    });

    private async Task SynchronizeAsync()
    {
        var cloud=_cloud??throw new InvalidOperationException("Sign in first.");
        await cloud.RequireMemberAsync("administrator",_lifetime.Token);
        _serverKey=await cloud.ServerKeyAsync(_lifetime.Token);
        foreach(var queued in _queue!.Snapshot().Where(x=>x.Status=="Queued"))
        {
            await cloud.SubmitAsync(queued.Change,_lifetime.Token);
            _queue.Put(queued with { Status="Pending",Message="Uploaded; awaiting main-server confirmation." });
        }
        foreach(var result in await cloud.MyResultsAsync(_lifetime.Token))
        {
            var queued=_queue.Snapshot().FirstOrDefault(row=>row.Change.Id==result.Id);
            if(queued!=null)_queue.Put(queued with { Status=result.Status,Message=result.Message.Length>0?result.Message:"Awaiting main-server confirmation." });
        }
        _students=await cloud.ProfilesAsync(_lifetime.Token);FilterStudents();
        RequestsList.ItemsSource=_queue.Snapshot().Reverse().ToArray();
        int pending=_queue.Snapshot().Count(x=>x.Status is "Queued" or "Pending");
        bool recent=cloud.LastServerPublication is DateTimeOffset last&&DateTimeOffset.UtcNow-last<TimeSpan.FromMinutes(2);
        SetStatus(recent?"Cloud synchronized":"No recent main-server publication",$"{_students.Count} profiles | {pending} changes awaiting confirmation | Last server publication: {cloud.LastServerPublication?.ToLocalTime().ToString("g")??"unavailable"}",
            recent?InfoBarSeverity.Success:InfoBarSeverity.Warning);
    }

    private void Search_Changed(object sender,TextChangedEventArgs e)=>FilterStudents();
    private void FilterStudents()
    {
        if(StudentsList==null)return;
        string text=SearchBox.Text.Trim();
        StudentsList.ItemsSource=_students.Where(x=>x.StudentId.Contains(text,StringComparison.OrdinalIgnoreCase)||x.FullName.Contains(text,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>x.FullName).ToArray();
    }

    private void Student_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(StudentsList.SelectedItem is not RemoteStudent student)return;
        _editing=student;StudentIdBox.IsReadOnly=true;StudentIdBox.Text=student.StudentId;FullNameBox.Text=student.FullName;
        StudentEmailBox.Text=student.Email;CourseBox.Text=student.Course;YearBox.Text=student.YearLevel;SectionBox.Text=student.Section;
        NfcUidBox.Text=student.NfcUid;EnrollmentBox.SelectedIndex=Array.IndexOf(new[]{"Active","Inactive","Graduated","Expelled","Pending Enrollment"},student.Status);
        NewPinBox.Password="";ReasonBox.Text="";ReissueQrBox.IsChecked=false;TemporaryBox.IsChecked=student.IsTemporary;_photo=student.PhotoBase64;
        _photoChanged=false;PhotoStatus.Text=student.PhotoOmitted?"Existing large photo retained on server":_photo.Length>0?"Existing student photo":"No student photo";EditorState.Text=$"{student.StudentId} | Server revision {student.Revision} | Clearance: {student.Clearance}";
    }

    private void ClearEditor()
    {
        _editing=null;StudentIdBox.IsReadOnly=false;StudentIdBox.Text="";FullNameBox.Text="";StudentEmailBox.Text="";
        CourseBox.Text="";YearBox.Text="";SectionBox.Text="";NfcUidBox.Text="";EnrollmentBox.SelectedIndex=4;NewPinBox.Password="";
        ReasonBox.Text="";ReissueQrBox.IsChecked=false;TemporaryBox.IsChecked=false;_photo="";_photoChanged=false;PhotoStatus.Text="No student photo";EditorState.Text="New student";
    }
    private void NewStudent_Click(object sender,RoutedEventArgs e){StudentsList.SelectedIndex=-1;ClearEditor();}

    private async void Submit_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(!_approved||_session==null||_queue==null||_serverKey==null)throw new InvalidOperationException("Sign in and synchronize first.");
        var change=new RemoteProfileChange(StudentIdBox.Text.Trim(),_editing?.Revision??0,_editing==null,FullNameBox.Text.Trim(),StudentEmailBox.Text.Trim(),
            CourseBox.Text.Trim(),YearBox.Text.Trim(),SectionBox.Text.Trim(),(EnrollmentBox.SelectedItem as ComboBoxItem)?.Content?.ToString()??"",
            NfcUidBox.Text.Trim(),TemporaryBox.IsChecked==true,_photo,NewPinBox.Password.Length>0?NewPinBox.Password:null,
            ReissueQrBox.IsChecked==true,ReasonBox.Text.Trim(),_photoChanged);
        change.Validate();
        bool qrIssuance=StudentProfileRules.ShouldIssueQr(change.StudentId,_editing?.NfcUid??"",_editing?.QrCredential??"",change.StudentId,change.NfcUid,change.ReissueQr);
        var dialog=new ContentDialog { Title="Submit student change?",Content=$"{change.StudentId}: {change.FullName}\nStatus: {change.Status}\nPIN reset: {change.NewPin!=null}\nQR issuance: {qrIssuance}\nThe change remains pending until the main server applies it.",
            PrimaryButtonText="Submit",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=XamlRoot };
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var envelope=RemoteChangeCrypto.Encrypt(change,Site,_session.Uid,_serverKey);
        _queue.Put(new(envelope,"Queued","Stored securely on this device; awaiting cloud upload.",change.StudentId));
        NewPinBox.Password="";ReissueQrBox.IsChecked=false;ReasonBox.Text="";
        RequestsList.ItemsSource=_queue.Snapshot().Reverse().ToArray();
        SetStatus("Change queued","Not yet applied by the main server.",InfoBarSeverity.Warning);
        await SynchronizeAsync();
    });

    private async void ShowQr_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(OwnerWindow==null||_editing==null)throw new InvalidOperationException("Select a student first.");
        var current=_students.FirstOrDefault(x=>x.StudentId==_editing.StudentId);
        if(string.IsNullOrWhiteSpace(current?.QrCredential))throw new InvalidOperationException("No server-issued QR is available. Synchronize after the change is applied.");
        await QrCredentialDisplay.ShowAsync(OwnerWindow,current.StudentId,current.QrCredential);
    });

    private async void ChoosePhoto_Click(object sender,RoutedEventArgs e)=>await RunAsync(async()=>
    {
        if(OwnerWindow==null)return;
        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".png");picker.FileTypeFilter.Add(".jpg");picker.FileTypeFilter.Add(".jpeg");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(OwnerWindow));
        var file=await picker.PickSingleFileAsync();if(file==null)return;
        var properties=await file.GetBasicPropertiesAsync();if(properties.Size>250000)throw new InvalidOperationException("Choose a photo smaller than 250 KB.");
        _photo=Convert.ToBase64String(await File.ReadAllBytesAsync(file.Path,_lifetime.Token));_photoChanged=true;PhotoStatus.Text=file.Name;
    });

    private void ScanCard_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(ScanCardButton.IsChecked!=true){StopScanning();return;}
            if(!_approved||ComPortBox.SelectedItem is not string port)throw new InvalidOperationException("Choose an NFC reader port.");
            if(!HardwareService.Connect(port))throw new InvalidOperationException("The NFC reader could not be opened.");
        }
        catch(Exception ex){StopScanning();SetStatus("NFC reader unavailable",ex.Message,InfoBarSeverity.Error);}
    }
    private void OnUid(string uid)=>DispatcherQueue.TryEnqueue(()=>
    {
        if(_closed||!_approved||ScanCardButton.IsChecked!=true)return;
        NfcUidBox.Text=uid.Trim();StopScanning();
    });
    private void StopScanning(){if(ScanCardButton.IsChecked==true)HardwareService.Disconnect();ScanCardButton.IsChecked=false;}
    private void Editor_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        bool narrow=e.NewSize.Width<680;
        Grid.SetColumn(CredentialFields,narrow?0:1);Grid.SetRow(CredentialFields,narrow?1:0);
        EditorGrid.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);
    }
    internal void Close()
    {
        _closed=true;_timer.Stop();_lifetime.Cancel();StopScanning();HardwareService.OnUidScanned-=OnUid;
        if(!IsServer||!RemoteAdministrationHost.Running)_session?.Dispose();
        NewPinBox.Password="";PasswordBox.Password="";
    }
}

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using BPM.App.ViewModels;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Application.Services;
using BPM.Domain.Models;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Delete_SelectionChangesDuringConfirmationOnlyDeletesConfirmedAccount() => OnSta(() =>
    {
        var fixture = new Fixture();
        MainViewModel? model = null;
        var confirmation = new Confirmation(name =>
        {
            name.Should().Be("A");
            model!.SelectedAccount = model.Accounts[1];
            return true;
        });
        model = fixture.CreateModel(confirmation);
        model.SelectedAccount = model.Accounts[0];
        var deletedId = fixture.First.Id;

        model.DeleteCommand.Execute(null);

        fixture.Config.Config.Accounts.Select(a => a.DisplayName).Should().Equal("B");
        fixture.Credentials.Exists("key-A").Should().BeFalse();
        fixture.Credentials.Exists("key-B").Should().BeTrue();
        fixture.Assist.Stopped.Should().Be(deletedId);
    });

    [Fact]
    public void Delete_TargetStartsWhileConfirmingDoesNotDeleteItsProfileOrCredential() => OnSta(() =>
    {
        var fixture = new Fixture();
        MainViewModel? model = null;
        model = fixture.CreateModel(new Confirmation(_ => { model!.Accounts[0].IsBusy = true; return true; }));
        model.SelectedAccount = model.Accounts[0];
        model.DeleteCommand.Execute(null);
        fixture.Config.Config.Accounts.Should().HaveCount(2);
        fixture.Credentials.Exists("key-A").Should().BeTrue();
        fixture.Assist.Stopped.Should().BeNull();
    });

    [Fact]
    public void Reload_DuringLaunchKeepsRowBusyAndCompletionUpdatesVisibleRow() => OnSta(() =>
    {
        var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        fixture.Launcher.OnLaunch = () =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        var model = fixture.CreateModel(new Confirmation(_ => true));
        var row = model.Accounts[0];
        model.LaunchCommand.Execute(row);
        try
        {
            entered.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            // 保存其他配置后，真实存储会返回新模型对象。
            fixture.Config.Config.Accounts[0] = new Account
            {
                Id = fixture.First.Id, DisplayName = "renamed", WindowsUser = "A", CredentialKey = "key-A", BattleNetPath = "fake.exe",
            };
            typeof(MainViewModel).GetMethod("Reload", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, null);
            model.Accounts[0].Should().BeSameAs(row);
            row.DisplayName.Should().Be("renamed");
            row.IsBusy.Should().BeTrue();
            model.LaunchCommand.CanExecute(row).Should().BeFalse();
            model.LaunchCommand.Execute(row); // 即使绕过 CanExecute，也不能重复启动。
            Volatile.Read(ref calls).Should().Be(1);
        }
        finally { release.Set(); PumpUntil(() => !row.IsBusy); }
        model.Accounts[0].Status.Should().Be("已启动");
        model.LaunchCommand.CanExecute(row).Should().BeTrue();
    });

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(12)).Should().BeTrue("STA regression must complete without a native dialog");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        completed().Should().BeTrue();
    }

    private sealed class Fixture
    {
        public MemoryConfig Config { get; } = new();
        public MemoryCredentials Credentials { get; } = new();
        public Launcher Launcher { get; } = new();
        public Assist Assist { get; } = new();
        public Account First => Config.Config.Accounts[0];
        public Fixture()
        {
            foreach (var name in new[] { "A", "B" })
            {
                Config.Config.Accounts.Add(new Account { DisplayName = name, WindowsUser = name, CredentialKey = "key-" + name, BattleNetPath = "fake.exe" });
                Credentials.Save("key-" + name, name, "test-only");
            }
        }
        public MainViewModel CreateModel(IAccountDeletionConfirmation confirmation)
        {
            var accounts = new AccountService(Config, Credentials, NullLogger<AccountService>.Instance);
            var launches = new LaunchService(Config, Credentials, new Adapter(), Launcher, NullLogger<LaunchService>.Instance);
            return new MainViewModel(accounts, launches, null!, null!, NullLogger<MainViewModel>.Instance, Assist, confirmation);
        }
    }
    private sealed class Confirmation(Func<string, bool> confirm) : IAccountDeletionConfirmation
    {
        public bool Confirm(string name) => confirm(name);
    }
    private sealed class MemoryConfig : IConfigService
    {
        public Config Config { get; private set; } = new();
        public Result<Config> Load() => Result<Config>.Ok(Config);
        public Result Save(Config config) { Config = config; return Result.Ok(); }
        public Result Export(string path) => throw new NotSupportedException();
        public Result<Config> Import(string path) => throw new NotSupportedException();
    }
    private sealed class MemoryCredentials : ICredentialService
    {
        private readonly Dictionary<string, StoredCredential> _items = new();
        public Result Save(string key, string user, string password) { _items[key] = new(user, password); return Result.Ok(); }
        public Result<StoredCredential> Get(string key) => Result<StoredCredential>.Ok(_items[key]);
        public Result Delete(string key) { _items.Remove(key); return Result.Ok(); }
        public bool Exists(string key) => _items.ContainsKey(key);
    }
    private sealed class Launcher : IProcessLauncher
    {
        public Action? OnLaunch { get; set; }
        public Result<int> Launch(LaunchRequest request) { OnLaunch?.Invoke(); return Result<int>.Ok(4242); }
    }
    private sealed class Adapter : IGamePlatformAdapter
    {
        public string PlatformName => "test";
        public Result ValidateInstallation(Account account) => Result.Ok();
        public Result<LaunchRequest> BuildLaunchRequest(Account account, StoredCredential credential) => Result<LaunchRequest>.Ok(new LaunchRequest
        {
            User = account.WindowsUser, Domain = ".", Password = credential.Password, ExecutablePath = account.BattleNetPath,
        });
    }
    private sealed class Assist : IBrowserLoginAssist
    {
        public Guid? Stopped { get; private set; }
        public event Action<LoginAssistStatus>? StatusChanged { add { } remove { } }
        public LoginAssistStatus Start(Account account) => new(account.Id, LoginAssistState.Ready, "ready");
        public LoginAssistStatus? GetStatus(Guid id) => null;
        public void Stop(Guid id) => Stopped = id;
        public Task WaitForIdleAsync() => Task.CompletedTask;
    }
}

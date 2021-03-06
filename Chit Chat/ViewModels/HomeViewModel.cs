using System;
using System.Threading;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using ChitChat.Models;
using System.Windows.Input;
using System.Collections.ObjectModel;
using ChitChat.Commands;
using ChitChat.Helper.Extensions;
using ChitChat.Helper.Exceptions;
using ChitChat.Helper;
using System.Windows;
using System.Net.Mail;
using Microsoft.AspNetCore.SignalR.Client;
using ChitChat.Events;
using Newtonsoft.Json;
using System.Security;
using System.Runtime.InteropServices;
using ChitChat.Services;

namespace ChitChat.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private bool _isConnecting = false;
        private bool _isRegistering = false;
        private string _currentUserName;
        private string _email;
        private string _displayName;
        private HubConnection connection;
        private UserModel _currentUser;
        private IHttpService _httpService;
        public EventHandler<ConnectionEventArgs> OnSuccessfulConnect;
        public EventHandler OnRegister;

        public HomeViewModel(IHttpService httpService, Logger logger)
        {
            _httpService = httpService;
            HomeLogger = logger;
            Password = new SecureString();
        }

        public Logger HomeLogger { get; }
        public bool IsConnecting
        {
            get => _isConnecting;
            set => SetPropertyValue(ref _isConnecting, value);
        }
        public bool IsRegistering
        {
            get => _isRegistering;
            set => SetPropertyValue(ref _isRegistering, value);
        }
        public string UserName
        {
            get => _currentUserName;
            set => SetPropertyValue(ref _currentUserName, value.Trim());
        }
        public SecureString Password { get; set; }

        public string Email
        {
            get => _email;
            set => SetPropertyValue(ref _email, value.Trim());
        }
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (value.Length > 20)
                    return;
                SetPropertyValue(ref _displayName, value);                    
            }
        }

        public ICommand Register => new RelayCommand(RegisterAccountAsync, CanRegisterAccount);
        public ICommand Login => new RelayCommand(LoginToServerAsync, CanLogin);


        private bool CanLogin() => !string.IsNullOrEmpty(_currentUserName) && Password.Length > 0 && !_isConnecting;

        private async Task LoginToServerAsync()
        {
            IsConnecting = true;
            _ = HomeLogger.LogMessage("Connecting...");
            try
            {        
                await Task.Run(async () =>
                {
                    var user = await _httpService.PostUserDataAsync("/api/chat/Login",
                         JsonConvert.SerializeObject(new UserCredentials(_currentUserName, Password.DecryptPassword())));

                    _currentUser = new UserModel { DisplayName = user.DisplayName, ProfilePicture = user.ProfilePicture };
                    BuildConnection();
                    CreateHandlers();
                    await connection.StartAsync();
                });
            }
            catch (HttpRequestException)
            {
                _ = HomeLogger.LogMessage($"Could not connect to the server.");
                IsConnecting = false;
            }
            catch (TaskCanceledException)
            {
                _ = HomeLogger.LogMessage($"Could not connect to the server.");
                IsConnecting = false;
            }
            catch (LoginException e)
            {
                _ = HomeLogger.LogMessage(e.Message);
                IsConnecting = false;
            }

        }

        private void BuildConnection()
        {
            connection = new HubConnectionBuilder()
                      .WithUrl("http://localhost:5001/chathub")
                      .Build();

        }

        private bool CanRegisterAccount() => !string.IsNullOrEmpty(UserName) &&
                Password.Length > 0 &&
                !string.IsNullOrEmpty(Email) &&
                !string.IsNullOrEmpty(DisplayName) &&
                !_isRegistering;

        private async Task RegisterAccountAsync()
        {
            try
            {
                IsRegistering = true;
                Email.Validate();

                await _httpService.PostUserDataAsync("/api/chat/PostUser",
                    JsonConvert.SerializeObject(new UserCredentials(UserName, Password.DecryptPassword(), Email, DisplayName)));


                _ = HomeLogger.LogMessage("Successfully Registered!");
                IsRegistering = false;
                ClearCredentials();

                OnRegister?.Invoke(this, EventArgs.Empty);
            }
            catch (FormatException)
            {
                _ = HomeLogger.LogMessage("Email Address is Invalid");
                IsRegistering = false;
            }
            catch (TaskCanceledException)
            {
                _ = HomeLogger.LogMessage("Could not connect to the server.");
                IsRegistering = false;
            }
            catch (HttpRequestException)
            {
                _ = HomeLogger.LogMessage("Could not connect to the server.");
                IsRegistering = false;
            }
            catch (RegistrationException e)
            {
                _ = HomeLogger.LogMessage(e.Message);
                IsRegistering = false;
            }
        }

        public void ClearCredentials()
        {
            UserName = "";
            Email = "";
            DisplayName = "";
            Password.Clear();         
        }
        private void CreateHandlers()
        {
            connection.On<DataModel>("Connected", (data) =>
            {

                // Invoke the handler from the UI thread.
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SetConnectionID(_currentUser);
                    ConvertRTFDataToMessages(data.Messages);
                    OnSuccessfulConnect?.Invoke(this, new ConnectionEventArgs
                    {
                        ChatViewModelContext = new ChatViewModel(data, _currentUser, connection, _httpService)
                    });
                });
                RemoveHandlers();
            });
        }

        private void ConvertRTFDataToMessages(IEnumerable<MessageModel> data)
        {
            data.ConvertRTFToFlowDocument();
        }

        private void SetConnectionID(UserModel user)
        {
            user.ConnectionID = connection.ConnectionId;
        }

        private void RemoveHandlers()
        {
           connection.Remove("Connected");
        }
    }
}

﻿/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string GetStatus();

		[OperationContract]
		string HandleCommand(string input);
	}

	internal sealed class WCF : IWCF, IDisposable {
		private static string URL = "net.tcp://127.0.0.1:1242/ASF";

		internal bool IsServerRunning => ServiceHost != null;

		private Client Client;
		private ServiceHost ServiceHost;

		public void Dispose() {
			StopClient();
			StopServer();
		}

		public string GetStatus() => Program.GlobalConfig.SteamOwnerID == 0 ? "{}" : Bot.GetAPIStatus();

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Program.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				return "Refusing to handle request because SteamOwnerID is not set!";
			}

			Bot bot = Bot.Bots.Values.FirstOrDefault();
			if (bot == null) {
				return "ERROR: No bots are enabled!";
			}

			string command = "!" + input;

			// TODO: This should be asynchronous, but for some reason Mono doesn't return any WCF output if it is
			// We must keep it synchronous until either Mono gets fixed, or culprit for freeze located (and corrected)
			string output = bot.Response(Program.GlobalConfig.SteamOwnerID, command).Result;

			Program.ArchiLogger.LogGenericInfo("Answered to WCF command: " + input + " with: " + output);
			return output;
		}

		internal static void Init() {
			if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHost)) {
				Program.GlobalConfig.WCFHost = Program.GetUserInput(ASF.EUserInputType.WCFHostname);
				if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHost)) {
					return;
				}
			}

			URL = "net.tcp://" + Program.GlobalConfig.WCFHost + ":" + Program.GlobalConfig.WCFPort + "/ASF";
		}

		internal string SendCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Program.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			Program.ArchiLogger.LogGenericInfo("Sending command: " + input + " to WCF server on " + URL + "...");

			if (Client == null) {
				Client = new Client(
					new NetTcpBinding {
						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None },
						SendTimeout = new TimeSpan(0, 5, 0)
					},
					new EndpointAddress(URL)
				);
			}

			return Client.HandleCommand(input);
		}

		internal void StartServer() {
			if (IsServerRunning) {
				return;
			}

			Program.ArchiLogger.LogGenericInfo("Starting WCF server on " + URL + "...");

			try {
				ServiceHost = new ServiceHost(typeof(WCF), new Uri(URL));
				ServiceHost.AddServiceEndpoint(
					typeof(IWCF),
					new NetTcpBinding {
						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None },
						SendTimeout = new TimeSpan(0, 5, 0)
					},
					string.Empty
				);
				ServiceHost.Open();

				Program.ArchiLogger.LogGenericInfo("WCF server ready!");
			} catch (AddressAccessDeniedException) {
				Program.ArchiLogger.LogGenericError("WCF service could not be started because of AddressAccessDeniedException!");
				Program.ArchiLogger.LogGenericWarning("If you want to use WCF service provided by ASF, consider starting ASF as administrator, or giving proper permissions!");
			} catch (Exception e) {
				Program.ArchiLogger.LogGenericException(e);
			}
		}

		internal void StopServer() {
			if (!IsServerRunning) {
				return;
			}

			if (ServiceHost.State != CommunicationState.Closed) {
				try {
					ServiceHost.Close();
				} catch (Exception e) {
					Program.ArchiLogger.LogGenericException(e);
				}
			}

			ServiceHost = null;
		}

		private void StopClient() {
			if (Client == null) {
				return;
			}

			if (Client.State != CommunicationState.Closed) {
				Client.Close();
			}

			Client = null;
		}
	}

	internal sealed class Client : ClientBase<IWCF> {
		internal Client(Binding binding, EndpointAddress address) : base(binding, address) { }

		internal string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Program.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			try {
				return Channel.HandleCommand(input);
			} catch (Exception e) {
				Program.ArchiLogger.LogGenericException(e);
				return null;
			}
		}
	}
}
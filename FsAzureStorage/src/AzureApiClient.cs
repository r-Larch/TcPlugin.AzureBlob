using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;


namespace FsAzureStorage {
    public class AzureApiClient {
        public async Task<IEnumerable<StorageConnectionString>> GetStorageAccounts(Action<string> log)
        {
            var config = new AdalConfiguration();
            var tokenCache = new TokenCache();
            var context = new AuthenticationContext(config.AdEndpoint + config.TenantId, config.ValidateAuthority, tokenCache);

            var userTokenProvider = new PopupUserTokenProvider(context, config, UserIdentifier.AnyUser);
            var tokenCredentials = new TokenCredentials(userTokenProvider);

            var subscriptions = await new SubscriptionClient(tokenCredentials).Subscriptions.ListAsync();

            var accounts = new List<Task<StorageConnectionString>>();
            foreach (var subscription in subscriptions) {
                log($"Subscription: {subscription.DisplayName}");

                var client = new StorageManagementClient(tokenCredentials) {SubscriptionId = subscription.SubscriptionId};
                var list = await client.StorageAccounts.ListAsync();

                accounts.AddRange(list.Select(async account => {
                    var resourceGroup = Regex.Replace(account.Id, @".*/resourceGroups/([^/]+)/providers/.*", "$1");
                    var result = await client.StorageAccounts.ListKeysAsync(resourceGroup, account.Name);
                    var key = result.Keys.FirstOrDefault(x => x.Permissions == KeyPermission.Full) ?? result.Keys.FirstOrDefault();

                    var uri = new Uri(account.PrimaryEndpoints.Blob);

                    log($"  {account.Name}");

                    return new StorageConnectionString {
                        DefaultEndpointsProtocol = uri.Scheme,
                        AccountName = account.Name,
                        AccountKey = key?.Value,
                        EndpointSuffix = uri.Host.Substring($"{account.Name}.blob.".Length) // => core.windows.net
                    };
                }));
            }

            return accounts.Select(_ => _.Result);
        }
    }


    public class PopupUserTokenProvider : ITokenProvider {
        private readonly AuthenticationContext _context;
        private readonly AdalConfiguration _config;
        private readonly UserIdentifier _userId;

        public PopupUserTokenProvider(AuthenticationContext context, AdalConfiguration config, UserIdentifier userId)
        {
            _context = context;
            _config = config;
            _userId = userId;
        }

        public void Clear()
        {
            NativeMethods.ClearAdalTokenCache();
        }

        public virtual async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                //var thread = new Thread(() => {//});
                //thread.SetApartmentState(ApartmentState.STA);
                //thread.Name = "AcquireTokenThread";
                //thread.Start();
                //thread.Join();
                // look private AuthenticationResult AcquireToken() Microsoft.Azure.Commands.Common.Authentication.dll class UserTokenProvider line: 110

                var authenticationResult = await _context.AcquireTokenAsync(
                    resource: _config.ResourceClientUri,
                    clientId: _config.ClientId,
                    redirectUri: _config.ClientRedirectUri,
                    parameters: new PlatformParameters(PromptBehavior.Auto),
                    userId: _userId
                ).ConfigureAwait(false);

                return new AuthenticationHeaderValue(authenticationResult.AccessTokenType, authenticationResult.AccessToken);
            }
            catch (AdalException ex) {
                throw new AuthenticationException("ErrorRenewingToken", ex);
            }
        }

        private static class NativeMethods {
            public static void ClearAdalTokenCache()
            {
                InternetSetOption(IntPtr.Zero, 42, IntPtr.Zero, 0);
            }

            [DllImport("wininet.dll", SetLastError = true)]
            private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int lpdwBufferLength);
        }
    }


    public class AdalConfiguration {
        public string AdEndpoint { get; set; }
        public bool ValidateAuthority { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public Uri ClientRedirectUri { get; set; }
        public string ResourceClientUri { get; set; }

        public static readonly Uri PowerShellRedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");
        public const string PowerShellClientId = "1950a258-227b-4e31-a9cf-717495945fc2";

        public AdalConfiguration()
        {
            ClientId = PowerShellClientId;
            ClientRedirectUri = PowerShellRedirectUri;
            ValidateAuthority = true;
            AdEndpoint = "https://login.microsoftonline.com/";
            ResourceClientUri = "https://management.core.windows.net/";
            TenantId = "Common";
        }
    }


    public class StorageConnectionString {
        public string DefaultEndpointsProtocol { get; set; } = "https";
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string EndpointSuffix { get; set; } = "core.windows.net";

        public string ConnectionString => this.ToString();

        public override string ToString()
        {
            return $"DefaultEndpointsProtocol={DefaultEndpointsProtocol};" +
                   $"AccountName={AccountName};" +
                   $"AccountKey={AccountKey};" +
                   $"EndpointSuffix={EndpointSuffix}";
        }
    }
}

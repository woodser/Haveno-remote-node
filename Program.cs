using Manta.Remote.Helpers;
using Manta.Remote.Services;

DaemonService daemonService = new();

await daemonService.GetHavenoAsync();

var password = PasswordHelper.GetPassword();

var daemonTask = Task.Run(() => daemonService.StartDaemonAsync(password));
var proxyTask = Task.Run(daemonService.StartReverseProxyAsync);

var host = await daemonService.GetOnionAddressAsync();

QrCodeHelper.PrintExternalIpAddressAndPassword(host, password);

Task.WaitAny([proxyTask, daemonTask]);

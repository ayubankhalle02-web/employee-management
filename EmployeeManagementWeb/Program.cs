using EmployeeManagementWeb;
using EmployeeManagementWeb.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<EmsService>();

await builder.Build().RunAsync();

# Employee Management System (web)

Blazor WebAssembly (C#) version of the Employee Management System. It runs entirely in the browser;
data is saved in the browser's localStorage (per browser, per device). Deployed to GitHub Pages by
`.github/workflows/deploy.yml` on every push to `main`.

Demo logins: `owner` / `owner123` and `employee` / `employee123`.

Run locally (needs the .NET 8 SDK):

    cd EmployeeManagementWeb
    dotnet run

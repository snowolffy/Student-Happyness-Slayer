Set-Location "f:\GitProject\StudentHappynessSlayer"
dotnet new sln -n OnionProcOparetor --force

dotnet sln OnionProcOparetor.sln add `
  src/OnionProcOparetor.Server/OnionProcOparetor.Server.csproj `
  src/OnionProcOparetor.Console/OnionProcOparetor.Console.csproj `
  src/OnionProcOparetor.Agent/OnionProcOparetor.Agent.csproj `
  src/OnionProcOparetor.AgentTray/OnionProcOparetor.AgentTray.csproj

dotnet build OnionProcOparetor.sln

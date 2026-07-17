using OnionProcOparetor.Agent.Services;

Console.WriteLine("OnionProcOparetor Agent starting");
var guid = ClientIdentity.GetOrCreateClientGuid();
Console.WriteLine($"ClientGuid: {guid}");

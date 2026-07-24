namespace OnionProcOparetor.Agent.Models;

/// <summary>Payload ของ command "UpdateSettings" - ตอนนี้มีแค่ poll interval override</summary>
public class UpdateSettingsPayload
{
    public int? PollIntervalOverrideSeconds { get; set; }
}

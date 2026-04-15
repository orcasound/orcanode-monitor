// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Text.Json;

namespace OrcanodeMonitor.Pages
{
    public class SocketXPNodeModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<SocketXPNodeModel> _logger;
        private string _deviceId;
        private string _jsonData;

        public SocketXPNodeModel(OrcanodeMonitorContext context, ILogger<SocketXPNodeModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _deviceId = string.Empty;
            _jsonData = string.Empty;
            DeviceName = string.Empty;
            DeviceGroup = string.Empty;
            DeviceStatus = string.Empty;
            CustomerName = string.Empty;
            CustomerSite = string.Empty;
            CreatedTime = string.Empty;
            ModifiedTime = string.Empty;
            AgentVersion = string.Empty;
            AgentDate = string.Empty;
            SysCpuModel = string.Empty;
            SysTotalMemory = string.Empty;
            SysTotalDisk = string.Empty;
            SysArch = string.Empty;
            SysOS = string.Empty;
            SysHostname = string.Empty;
            SysPlatform = string.Empty;
            SysInterfaceName = string.Empty;
            SysMacAddress = string.Empty;
            SysKernelVersion = string.Empty;
        }

        public string LastChecked
        {
            get
            {
                MonitorState monitorState = MonitorState.GetFrom(_databaseContext);

                if (monitorState.LastUpdatedTimestampUtc == null)
                {
                    return "";
                }
                return Fetcher.UtcToLocalDateTime(monitorState.LastUpdatedTimestampUtc)?.ToString() ?? "";
            }
        }

        // Parsed properties from JSON
        public string DeviceName { get; private set; }
        public string DeviceGroup { get; private set; }
        public string DeviceStatus { get; private set; }
        public string CustomerName { get; private set; }
        public string CustomerSite { get; private set; }
        public string CreatedTime { get; private set; }
        public string ModifiedTime { get; private set; }
        public string AgentVersion { get; private set; }
        public string AgentDate { get; private set; }
        public string SysCpuModel { get; private set; }
        public string SysTotalMemory { get; private set; }
        public string SysTotalDisk { get; private set; }
        public string SysArch { get; private set; }
        public string SysOS { get; private set; }
        public string SysHostname { get; private set; }
        public string SysPlatform { get; private set; }
        public string SysInterfaceName { get; private set; }
        public string SysMacAddress { get; private set; }
        public string SysKernelVersion { get; private set; }

        public async Task<IActionResult> OnGetAsync(string deviceId)
        {
            _deviceId = deviceId;
            string rawJson = await SocketXPFetcher.GetSocketXPDataAsync(deviceId, _logger);
            if (rawJson.IsNullOrEmpty())
            {
                return NotFound(); // Returns a 404 error page
            }

            // Parse JSON to extract fields
            try
            {
                using JsonDocument doc = JsonDocument.Parse(rawJson);
                JsonElement root = doc.RootElement;
                JsonElement deviceElement = root;

                // Check if it's wrapped in a "Devices" array
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Devices", out JsonElement devicesArray))
                {
                    // It's a response with a "Devices" array
                    if (devicesArray.ValueKind == JsonValueKind.Array && devicesArray.GetArrayLength() > 0)
                    {
                        deviceElement = devicesArray[0]; // Get first device
                    }
                    else
                    {
                        _logger.LogWarning("Devices array is empty for device {DeviceId}", deviceId);
                        return NotFound();
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    // It's a direct array of devices
                    deviceElement = root[0];
                }
                else if (root.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogError("Unexpected JSON structure for device {DeviceId}: {ValueKind}", deviceId, root.ValueKind);
                    return NotFound();
                }

                // Extract properties from deviceElement (excluding DeviceKey)
                DeviceName = deviceElement.TryGetProperty("DeviceName", out var deviceNameProp) ? deviceNameProp.GetString() ?? string.Empty : string.Empty;
                DeviceGroup = deviceElement.TryGetProperty("DeviceGroup", out var deviceGroupProp) ? deviceGroupProp.GetString() ?? string.Empty : string.Empty;
                DeviceStatus = deviceElement.TryGetProperty("DeviceStatus", out var deviceStatusProp) ? deviceStatusProp.GetString() ?? string.Empty : string.Empty;
                CustomerName = deviceElement.TryGetProperty("CustomerName", out var customerNameProp) ? customerNameProp.GetString() ?? string.Empty : string.Empty;
                CustomerSite = deviceElement.TryGetProperty("CustomerSite", out var customerSiteProp) ? customerSiteProp.GetString() ?? string.Empty : string.Empty;
                CreatedTime = deviceElement.TryGetProperty("CreatedTime", out var createdTimeProp) ? createdTimeProp.GetString() ?? string.Empty : string.Empty;
                ModifiedTime = deviceElement.TryGetProperty("ModifiedTime", out var modifiedTimeProp) ? modifiedTimeProp.GetString() ?? string.Empty : string.Empty;
                AgentVersion = deviceElement.TryGetProperty("AgentVersion", out var agentVersionProp) ? agentVersionProp.GetString() ?? string.Empty : string.Empty;
                AgentDate = deviceElement.TryGetProperty("AgentDate", out var agentDateProp) ? agentDateProp.GetString() ?? string.Empty : string.Empty;
                SysCpuModel = deviceElement.TryGetProperty("SysCpuModel", out var sysCpuModelProp) ? sysCpuModelProp.GetString() ?? string.Empty : string.Empty;
                SysTotalMemory = deviceElement.TryGetProperty("SysTotalMemory", out var sysTotalMemoryProp) ? sysTotalMemoryProp.ToString() : string.Empty;
                SysTotalDisk = deviceElement.TryGetProperty("SysTotalDisk", out var sysTotalDiskProp) ? sysTotalDiskProp.ToString() : string.Empty;
                SysArch = deviceElement.TryGetProperty("SysArch", out var sysArchProp) ? sysArchProp.GetString() ?? string.Empty : string.Empty;
                SysOS = deviceElement.TryGetProperty("SysOS", out var sysOSProp) ? sysOSProp.GetString() ?? string.Empty : string.Empty;
                SysHostname = deviceElement.TryGetProperty("SysHostname", out var sysHostnameProp) ? sysHostnameProp.GetString() ?? string.Empty : string.Empty;
                SysPlatform = deviceElement.TryGetProperty("SysPlatform", out var sysPlatformProp) ? sysPlatformProp.GetString() ?? string.Empty : string.Empty;
                SysInterfaceName = deviceElement.TryGetProperty("SysInterfaceName", out var sysInterfaceNameProp) ? sysInterfaceNameProp.GetString() ?? string.Empty : string.Empty;
                SysMacAddress = deviceElement.TryGetProperty("SysMacAddress", out var sysMacAddressProp) ? sysMacAddressProp.GetString() ?? string.Empty : string.Empty;
                SysKernelVersion = deviceElement.TryGetProperty("SysKernelVersion", out var sysKernelVersionProp) ? sysKernelVersionProp.GetString() ?? string.Empty : string.Empty;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse SocketXP JSON for device {DeviceId}", deviceId);
                return NotFound();
            }

            var formatter = new JsonFormatter();
            _jsonData = formatter.FormatJson(rawJson);
            return Page();
        }
    }
}

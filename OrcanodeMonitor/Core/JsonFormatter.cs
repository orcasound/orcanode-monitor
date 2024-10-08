// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class JsonFormatter
    {
        public string FormatJson(string jsonString)
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            return JsonSerializer.Serialize(jsonElement, options);
        }
    }
}

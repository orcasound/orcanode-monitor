// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Core
{
    public class State
    {
        public static DateTime? LastUpdatedTimestamp { get; set; }
    }
}

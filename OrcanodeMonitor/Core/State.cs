// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
namespace OrcanodeMonitor.Core
{
    public class State
    {
        static EnumerateNodesResult? lastResult;
        public static void SetLastResult(EnumerateNodesResult result)
        {
            lastResult = result;
        }
        public static EnumerateNodesResult? LastResult => lastResult;

        // TODO: persist this state across restarts.
    }
}

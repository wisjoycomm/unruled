// Copyright (C) KitWright. Licensed under MIT.
using System.Collections.Generic;

namespace KitWright.Editor.Tools
{
    internal class FunctionCall
    {
        public string FunctionName { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }
}

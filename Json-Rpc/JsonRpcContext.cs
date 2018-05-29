using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Provides access to a context specific to each JsonRpc method invocation.
    /// This is a convienence class that wraps calls to Context specific methods on AustinHarris.JsonRpc.Handler
    /// </summary>
    public class JsonRpcContext
    {
        private JsonRpcContext(object value)
        {
            Value = value;
        }

        /// <summary>
        /// The data associated with this context.
        /// </summary>
        public object Value { get; private set; }
    }
}

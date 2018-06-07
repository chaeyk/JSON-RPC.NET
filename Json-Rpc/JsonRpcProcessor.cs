using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        public static void Process(JsonRpcStateAsync async, object context = null)
        {
            Process(Handler.DefaultSessionId(), async, context);
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null)
        {
            Process(sessionId, async.JsonRpc, context)
                .ContinueWith(t =>
            {
                async.Result = t.Result;
                async.SetCompleted();
            });
        }
        public static async Task<string> Process(string jsonRpc, object context = null)
        {
            return await Process(Handler.DefaultSessionId(), jsonRpc, context);
        }
        public static async Task<string> Process(string sessionId, string jsonRpc, object context = null)
        {
            return await ProcessInternal(sessionId, jsonRpc, context);
        }

        public static async Task<JsonResponse> Process(string jsonRpc, JsonRequest jsonRequest, object context = null)
        {
            return await Process(Handler.DefaultSessionId(), jsonRpc, jsonRequest, context);
        }

        public static async Task<JsonResponse> Process(string sessionId, string jsonRpc, JsonRequest jsonRequest, object context = null)
        {
            return await ProcessInternal(sessionId, jsonRpc, jsonRequest, context);
        }

        public static async Task<JsonResponse[]> Process(string jsonRpc, JsonRequest[] jsonRequests, object context = null)
        {
            return await Process(Handler.DefaultSessionId(), jsonRpc, jsonRequests, context);
        }

        public static async Task<JsonResponse[]> Process(string sessionId, string jsonRpc, JsonRequest[] jsonRequests, object context = null)
        {
            return await ProcessInternal(sessionId, jsonRpc, jsonRequests, context);
        }

        private static async Task<string> ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);

            JsonRequest[] batch = null;
            bool singleBatch;
            try
            {
                batch = DeserializeRequest(jsonRpc, out singleBatch);
            }
            catch (Exception ex)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                });
            }

            JsonResponse[] jsonResponses = await ProcessInternal(sessionId, jsonRpc, batch, jsonRpcContext);
            return SerializeResponse(jsonResponses, singleBatch);
        }

        private static async Task<JsonResponse[]> ProcessInternal(string sessionId, string jsonRpc, JsonRequest[] jsonRequests, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);

            if (jsonRequests.Length == 0)
            {
                return new JsonResponse[]
                {
                    new JsonResponse
                    {
                        Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(-32600, "Invalid Request", "Batch of calls was empty."))
                    }
                };
            }

            List<JsonResponse> jsonResponses = null;
            for (var i = 0; i < jsonRequests.Length; i++)
            {
                var jsonRequest = jsonRequests[i];
                var jsonResponse = await ProcessInternal(sessionId, jsonRpc, jsonRequest, jsonRpcContext);

                // single rpc optimization
                if (jsonRequests.Length == 1 && (jsonResponse.Id != null || jsonResponse.Error != null))
                {
                    return new JsonResponse[] { jsonResponse };
                }

                if (jsonResponses == null)
                {
                    jsonResponses = new List<JsonResponse>();
                }
                jsonResponses.Add(jsonResponse);
            }
            return jsonResponses.ToArray();
        }

        private static async Task<JsonResponse> ProcessInternal(string sessionId, string jsonRpc, JsonRequest jsonRequest, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);

            var jsonResponse = new JsonResponse();

            if (jsonRequest == null)
            {
                jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                    new JsonRpcException(-32700, "Parse error",
                        "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
            }
            else if (jsonRequest.Method == null)
            {
                jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                    new JsonRpcException(-32601, "Invalid Request", "Missing property 'method'"));
            }
            else
            {
                jsonResponse.Id = jsonRequest.Id;

                var data = await handler.Handle(jsonRequest, jsonRpcContext);

                if (data == null) return null;

                jsonResponse.Error = data.Error;
                jsonResponse.Result = data.Result;
            }
            return jsonResponse;
        }

        private static bool IsSingleRpc(string json)
        {
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') return true;
                else if (json[i] == '[') return false;
            }
            return true;
        }

        public static JsonRequest[] DeserializeRequest(string jsonRpc, out bool isSingleRpc)
        {
            JsonRequest[] batch = null;
            try
            {
                isSingleRpc = IsSingleRpc(jsonRpc);
                if (isSingleRpc)
                {
                    var foo = JsonConvert.DeserializeObject<JsonRequest>(jsonRpc);
                    batch = new[] { foo };
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc);
                }
            }
            catch (Exception ex)
            {
                throw new JsonRpcException(-32700, "Parse error", ex);
            }
            return batch;
        }

        public static string SerializeResponse(JsonResponse jsonResponse)
        {
            if (jsonResponse.Id == null && jsonResponse.Error == null)
            {
                // notification returns empty string
                return "";
            }

            if (jsonResponse.Result == null && jsonResponse.Error == null)
            {
                // Per json rpc 2.0 spec
                // result : This member is REQUIRED on success.
                // This member MUST NOT exist if there was an error invoking the method.    
                // Either the result member or error member MUST be included, but both members MUST NOT be included.
                jsonResponse.Result = new Newtonsoft.Json.Linq.JValue((Object)null);
            }

            StringWriter sw = new StringWriter();
            JsonTextWriter writer = new JsonTextWriter(sw);
            writer.WriteStartObject();
            writer.WritePropertyName("jsonrpc"); writer.WriteValue("2.0");

            if (jsonResponse.Error != null)
            {
                writer.WritePropertyName("error"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Error));
            }
            else
            {
                writer.WritePropertyName("result"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Result));
            }
            writer.WritePropertyName("id"); writer.WriteValue(jsonResponse.Id);
            writer.WriteEndObject();
            return sw.ToString();
        }

        public static string SerializeResponse(IEnumerable<JsonResponse> jsonResponses, bool isSingleRpc)
        {
            if (isSingleRpc)
            {
                return SerializeResponse(jsonResponses.First<JsonResponse>());
            }

            StringBuilder sbResult = new StringBuilder(0);
            foreach (JsonResponse jsonResponse in jsonResponses)
            {
                string str = SerializeResponse(jsonResponse);
                if (str.Length == 0)
                {
                    // this is notification
                    continue;
                }

                sbResult.Append(sbResult.Length == 0 ? "[" : ",");
                sbResult.Append(str);
            }
            if (sbResult.Length > 0)
            {
                sbResult.Append("]");
            }
            return sbResult.ToString();
        }
    }
}

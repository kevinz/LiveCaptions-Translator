﻿using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    public static class TranslateAPI
    {
        /*
         * The key of this field is used as the content for `translateAPIBox` in the `SettingPage`.
         * If you'd like to add a new API, please insert the key-value pair here.
         */
        public static readonly Dictionary<string, Func<string, CancellationToken, Task<string>>>
            TRANSLATE_FUNCTIONS = new()
        {
            { "Google", Google },
            { "Google2", Google2 },
            { "Ollama", Ollama },
            { "OpenAI", OpenAI },
            { "DeepL", DeepL },
            { "OpenRouter", OpenRouter },
            { "Youdao", Youdao },
            { "MTranServer", MTranServer },
            { "Baidu", Baidu },
            { "LibreTranslate", LibreTranslate },
        };
        public static readonly List<string> LLM_BASED_APIS = new()
        {
            "Ollama", "OpenAI", "OpenRouter"
        };
        public static readonly List<string> OUT_OF_THE_BOX_APIS = new()
        {
            "Google", "Google2"
        };

        public static Func<string, CancellationToken, Task<string>> TranslateFunction => 
            TRANSLATE_FUNCTIONS[Translator.Setting.ApiName];
        public static bool IsLLMBased => LLM_BASED_APIS.Contains(Translator.Setting.ApiName);
        public static string Prompt => Translator.Setting.Prompt;

        private static readonly HttpClient client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static async Task<string> OpenAI(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["OpenAI"] as OpenAIConfig;
            string language = OpenAIConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;

            var messages = new List<BaseLLMConfig.Message>
            {
                new BaseLLMConfig.Message { role = "system", content = string.Format(Prompt, language) },
                new BaseLLMConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
            };
            if (Translator.Setting.ContextAware)
            {
                foreach (var entry in Translator.Caption.DisplayContexts)
                {
                    string translatedText = entry.TranslatedText;
                    if (translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                        continue;
                    translatedText = RegexPatterns.NoticePrefix().Replace(translatedText, "");
                        
                    messages.InsertRange(1, [
                        new BaseLLMConfig.Message { role = "user", content = $"🔤 {entry.SourceText} 🔤" },
                        new BaseLLMConfig.Message { role = "assistant", content = $"{translatedText}" }
                    ]);
                }
            }
            
            var requestData = new
            {
                model = config?.ModelName,
                messages = messages,
                temperature = config?.Temperature,
                max_tokens = 64,
                stream = false
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config?.ApiKey}");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(TextUtil.NormalizeUrl(config?.ApiUrl), content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<OpenAIConfig.Response>(responseString);
                var output = responseObj.choices[0].message.content;
                return RegexPatterns.ModelThinking().Replace(output, "");
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }
        
        public static async Task<string> Ollama(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Ollama"] as OllamaConfig;
            string language = OllamaConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
            string apiUrl = $"http://{config.Ip}:{config.Port}/api/chat";
            
            var messages = new List<BaseLLMConfig.Message>
            {
                new BaseLLMConfig.Message { role = "system", content = string.Format(Prompt, language) },
                new BaseLLMConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
            };
            if (Translator.Setting.ContextAware)
            {
                foreach (var entry in Translator.Caption.DisplayContexts)
                {
                    string translatedText = entry.TranslatedText;
                    if (translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                        continue;
                    translatedText = RegexPatterns.NoticePrefix().Replace(translatedText, "");
                        
                    messages.InsertRange(1, [
                        new BaseLLMConfig.Message { role = "user", content = $"🔤 {entry.SourceText} 🔤" },
                        new BaseLLMConfig.Message { role = "assistant", content = $"{translatedText}" }
                    ]);
                }
            }

            var requestData = new
            {
                model = config?.ModelName,
                messages = messages,
                temperature = config?.Temperature,
                max_tokens = 64,
                stream = false
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Clear();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(apiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<OllamaConfig.Response>(responseString);
                var output = responseObj.message.content;
                return RegexPatterns.ModelThinking().Replace(output, "");
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }
        
        public static async Task<string> OpenRouter(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["OpenRouter"] as OpenRouterConfig;
            string language = OpenRouterConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
            string apiUrl = "https://openrouter.ai/api/v1/chat/completions";
            
            var messages = new List<BaseLLMConfig.Message>
            {
                new BaseLLMConfig.Message { role = "system", content = string.Format(Prompt, language) },
                new BaseLLMConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
            };
            if (Translator.Setting.ContextAware)
            {
                foreach (var entry in Translator.Caption.DisplayContexts)
                {
                    string translatedText = entry.TranslatedText;
                    if (translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                        continue;
                    translatedText = RegexPatterns.NoticePrefix().Replace(translatedText, "");
                        
                    messages.InsertRange(1, [
                        new BaseLLMConfig.Message { role = "user", content = $"🔤 {entry.SourceText} 🔤" },
                        new BaseLLMConfig.Message { role = "assistant", content = $"{translatedText}" }
                    ]);
                }
            }

            var requestData = new
            {
                model = config?.ModelName,
                messages = messages
            };

            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            request.Headers.Add("Authorization", $"Bearer {config?.ApiKey}");
            request.Headers.Add("HTTP-Referer", "https://github.com/SakiRinn/LiveCaptionsTranslator");
            request.Headers.Add("X-Title", "LiveCaptionsTranslator");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var output = jsonResponse.GetProperty("choices")[0]
                                         .GetProperty("message")
                                         .GetProperty("content")
                                         .GetString() ?? string.Empty;
                return RegexPatterns.ModelThinking().Replace(output, "");
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }
        
        public static async Task<string> Google(string text, CancellationToken token = default)
        {
            var language = Translator.Setting?.TargetLanguage;

            string encodedText = Uri.EscapeDataString(text);
            var url = $"https://clients5.google.com/translate_a/t?" +
                      $"client=dict-chrome-ex&sl=auto&" +
                      $"tl={language}&" +
                      $"q={encodedText}";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();

                var responseObj = JsonSerializer.Deserialize<List<List<string>>>(responseString);

                string translatedText = responseObj[0][0];
                return translatedText;
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }

        public static async Task<string> Google2(string text, CancellationToken token = default)
        {
            string apiKey = "AIzaSyA6EEtrDCfBkHV8uU2lgGY-N383ZgAOo7Y";
            var language = Translator.Setting?.TargetLanguage;
            string strategy = "2";

            string encodedText = Uri.EscapeDataString(text);
            string url = $"https://dictionaryextension-pa.googleapis.com/v1/dictionaryExtensionData?" +
                         $"language={language}&" +
                         $"key={apiKey}&" +
                         $"term={encodedText}&" +
                         $"strategy={strategy}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-referer", "chrome-extension://mgijmajocgfcbeboacabfgobmjgjcoja");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();

                using var jsonDoc = JsonDocument.Parse(responseBody);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("translateResponse", out JsonElement translateResponse))
                {
                    string translatedText = translateResponse.GetProperty("translateText").GetString();
                    return translatedText;
                }
                else
                    return "[ERROR] Translation Failed: Unexpected API response format";
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }

        public static async Task<string> DeepL(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["DeepL"] as DeepLConfig;
            string language = DeepLConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
            string apiUrl = TextUtil.NormalizeUrl(config.ApiUrl);

            var requestData = new
            {
                text = new[] { text },
                target_lang = language
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"DeepL-Auth-Key {config?.ApiKey}");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(apiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);

                if (doc.RootElement.TryGetProperty("translations", out var translations) &&
                    translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0)
                {
                    return translations[0].GetProperty("text").GetString();
                }
                return "[ERROR] Translation Failed: No valid feedback";
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }


        public static async Task<string> Youdao(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Youdao"] as YoudaoConfig;
            string language = YoudaoConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;

            string salt = DateTime.Now.Millisecond.ToString();
            string sign = BitConverter.ToString(
                MD5.Create().ComputeHash(
                    Encoding.UTF8.GetBytes($"{config.AppKey}{text}{salt}{config.AppSecret}"))).Replace("-", "").ToLower();

            var parameters = new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = language,
                ["appKey"] = config.AppKey,
                ["salt"] = salt,
                ["sign"] = sign
            };

            var content = new FormUrlEncodedContent(parameters);
            client.DefaultRequestHeaders.Clear();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(config.ApiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<YoudaoConfig.TranslationResult>(responseString);

                if (responseObj.errorCode != "0")
                    return $"[ERROR] Translation Failed: Youdao Error - {responseObj.errorCode}";

                return responseObj.translation?.FirstOrDefault() ?? "[ERROR] Translation Failed: No content";
            }
            else
            {
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
            }
        }

        public static async Task<string> MTranServer(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["MTranServer"] as MTranServerConfig;
            string targetLanguage = MTranServerConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
            string sourceLanguage = config.SourceLanguage;
            string apiUrl = TextUtil.NormalizeUrl(config.ApiUrl);

            var requestData = new
            {
                text = text,
                to = targetLanguage,
                from = sourceLanguage
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config?.ApiKey}");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(apiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<MTranServerConfig.Response>(responseString);
                return responseObj.result;
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }

        public static async Task<string> Baidu(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Baidu"] as BaiduConfig;
            string language = BaiduConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;

            string salt = DateTime.Now.Millisecond.ToString();
            string sign = BitConverter.ToString(
                MD5.Create().ComputeHash(
                    Encoding.UTF8.GetBytes($"{config.AppId}{text}{salt}{config.AppSecret}"))).Replace("-", "").ToLower();

            var parameters = new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = language,
                ["appid"] = config.AppId,
                ["salt"] = salt,
                ["sign"] = sign
            };

            var content = new FormUrlEncodedContent(parameters);
            client.DefaultRequestHeaders.Clear();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(config.ApiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<BaiduConfig.TranslationResult>(responseString);

                if (responseObj.error_code is not null && responseObj.error_code != "0")
                    return $"[ERROR] Translation Failed: Baidu Error - {responseObj.error_code}";

                return responseObj.trans_result?.FirstOrDefault()?.dst ?? "[ERROR] Translation Failed: No content";
            }
            else
            {
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
            }
        }

        public static async Task<string> LibreTranslate(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["LibreTranslate"] as LibreTranslateConfig;
            string targetLanguage = LibreTranslateConfig.SupportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
            string apiUrl = TextUtil.NormalizeUrl(config.ApiUrl);

            var requestData = new
            {
                q = text,
                target = targetLanguage,
                source = "auto",
                format = "text",
                api_key = config?.ApiKey
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            client.DefaultRequestHeaders.Clear();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(apiUrl, content, token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.Message.StartsWith("The request"))
                    return $"[ERROR] Translation Failed: The request was canceled due to timeout (> 5 seconds), please use a faster API.";
                throw;
            }
            catch (Exception ex)
            {
                return $"[ERROR] Translation Failed: {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<LibreTranslateConfig.Response>(responseString);
                return responseObj.translatedText;
            }
            else
                return $"[ERROR] Translation Failed: HTTP Error - {response.StatusCode}";
        }
    }

    public class ConfigDictConverter : JsonConverter<Dictionary<string, List<TranslateAPIConfig>>>
    {
        public override Dictionary<string, List<TranslateAPIConfig>> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected a StartObject token.");
            var configs = new Dictionary<string, List<TranslateAPIConfig>>();

            reader.Read();
            while (reader.TokenType == JsonTokenType.PropertyName)
            {
                string key = reader.GetString();
                reader.Read();

                var configType = Type.GetType($"LiveCaptionsTranslator.models.{key}Config");
                TranslateAPIConfig config;

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var list = new List<TranslateAPIConfig>();
                    reader.Read();

                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                            config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, configType, options);
                        else
                            config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, typeof(TranslateAPIConfig), options);

                        list.Add(config);
                        reader.Read();
                    }
                    configs[key] = list;
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                        config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, configType, options);
                    else
                        config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, typeof(TranslateAPIConfig), options);
                    configs[key] = [config];
                }
                else
                    throw new JsonException("Expected a StartObject token or a StartArray token.");
                
                reader.Read();
            }

            if (reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException("Expected an EndObject token.");
            return configs;
        }

        public override void Write(
            Utf8JsonWriter writer, Dictionary<string, List<TranslateAPIConfig>> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in value)
            {
                writer.WritePropertyName(kvp.Key);
                var configType = Type.GetType($"LiveCaptionsTranslator.models.{kvp.Key}Config");

                if (kvp.Value is IEnumerable<TranslateAPIConfig> configList)
                {
                    writer.WriteStartArray();
                    foreach (var config in configList)
                    {
                        if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                            JsonSerializer.Serialize(writer, config, configType, options);
                        else
                            JsonSerializer.Serialize(writer, config, typeof(TranslateAPIConfig), options);
                    }
                    writer.WriteEndArray();
                }
                else
                    throw new JsonException($"Unsupported config type: {kvp.Value.GetType()}");
            }
            writer.WriteEndObject();
        }
    }
}

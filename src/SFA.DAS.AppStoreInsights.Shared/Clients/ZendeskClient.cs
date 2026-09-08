using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SFA.DAS.AppStoreInsights.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    [ExcludeFromCodeCoverage]
    public class ZendeskClient : IZendeskClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ZendeskClient> _logger;
        private readonly string _subdomain;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ZendeskFieldIds _fieldIds;

        private string _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        private readonly SemaphoreSlim _tokenSemaphore = new SemaphoreSlim(1, 1);

        public ZendeskClient(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<ZendeskClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _subdomain = config["Zendesk:Subdomain"]
                ?? throw new InvalidOperationException("Zendesk:Subdomain missing");

            _clientId = config["Zendesk:ClientId"]
                ?? throw new InvalidOperationException("Zendesk:ClientId missing");

            _clientSecret = config["Zendesk:ClientSecret"]
                ?? throw new InvalidOperationException("Zendesk:ClientSecret missing");

            var baseUrl = $"https://{_subdomain}.zendesk.com/api/v2/";
            _httpClient.BaseAddress = new Uri(baseUrl);

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var environment = config["EnvironmentName"] ?? "LOCAL";
            _fieldIds = ZendeskFieldIds.GetFieldIdsForEnvironment(environment);

            _logger.LogInformation(
                "ZendeskClient initialized for subdomain {Subdomain}, environment {Environment}",
                _subdomain,
                environment);
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            await _tokenSemaphore.WaitAsync(cancellationToken);

            try
            {
                if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
                {
                    return _accessToken;
                }

                _logger.LogInformation("Obtaining new Zendesk OAuth token");

                var tokenUrl = $"https://{_subdomain}.zendesk.com/oauth/tokens";

                var formData = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["scope"] = "read write"
                };

                using var content = new FormUrlEncodedContent(formData);

                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);

                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                request.Content = content;

                _logger.LogDebug("Requesting Zendesk OAuth token from {TokenUrl}", tokenUrl);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Zendesk OAuth token request failed. StatusCode: {StatusCode}",
                        (int)response.StatusCode);

                    throw new HttpRequestException(
                        $"Zendesk OAuth token request failed: {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                }

                _logger.LogDebug("Zendesk OAuth token request succeeded");

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (!root.TryGetProperty("access_token", out var tokenElement))
                {
                    throw new InvalidOperationException("Zendesk OAuth response did not contain 'access_token'.");
                }

                var accessToken = tokenElement.GetString();

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException("Zendesk OAuth returned an empty access token.");
                }

                var expiresIn = 3600;

                if (root.TryGetProperty("expires_in", out var expiresElement) &&
                    expiresElement.ValueKind == JsonValueKind.Number)
                {
                    expiresIn = expiresElement.GetInt32();
                }

                var refreshSeconds = Math.Max(60, expiresIn - 60);

                _accessToken = accessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(refreshSeconds);

                _logger.LogInformation(
                    "Zendesk OAuth token obtained successfully. Token expires at {TokenExpiry}",
                    _tokenExpiry);

                return _accessToken;
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<string> CreateTicketAsync(ZendeskTicket ticket, CancellationToken cancellationToken = default)
        {
            if (ticket is null)
                throw new ArgumentNullException(nameof(ticket));

            await EnsureAuthenticatedAsync(cancellationToken);

            var customFields = new List<object>();

            AddCustomField(customFields, _fieldIds.RatingApp, ticket.RatingApp);
            AddCustomField(customFields, _fieldIds.FeedbackApp, ticket.FeedbackApp);
            AddCustomField(customFields, _fieldIds.FeedbackIdApp, ticket.FeedbackIdApp);
            AddCustomField(customFields, _fieldIds.AppStore, ticket.AppStore);
            AddCustomField(customFields, _fieldIds.ResponseApp, ticket.ResponseApp);
            AddCustomField(customFields, _fieldIds.Subject, ticket.Subject);
            AddCustomField(customFields, _fieldIds.Description, ticket.Comment);

            var payload = new
            {
                ticket = new
                {
                    subject = ticket.Subject,
                    comment = new
                    {
                        body = BuildCommentBody(ticket)
                    },
                    requester = new
                    {
                        name = ticket.RequesterName,
                        email = ticket.RequesterEmail ?? $"{ticket.RequesterName?.Replace(" ", "")}@anonymous.zendesk.com"
                    },
                    priority = ticket.Priority,
                    tags = ticket.Tags,
                    custom_fields = customFields.Count > 0 ? customFields : null
                }
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync("tickets.json", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogError(
                    "Zendesk API error creating ticket. StatusCode: {StatusCode}",
                    (int)response.StatusCode);

                throw new HttpRequestException(
                    $"Failed to create ticket: {(int)response.StatusCode} {response.StatusCode} - {error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(responseJson);

            var ticketId = document.RootElement
                .GetProperty("ticket")
                .GetProperty("id")
                .ToString();

            _logger.LogInformation("Created Zendesk ticket with ID: {TicketId}", ticketId);

            return ticketId;
        }

        private void AddCustomField(List<object> fields, long? fieldId, string value)
        {
            if (fieldId.HasValue && !string.IsNullOrEmpty(value))
            {
                fields.Add(new { id = fieldId.Value, value });
            }
        }

        private string BuildCommentBody(ZendeskTicket ticket)
        {
            var sb = new StringBuilder();

            sb.AppendLine("App Feedback");
            sb.AppendLine("============");
            sb.AppendLine();

            sb.AppendLine($"Rating: {ticket.RatingApp}");
            sb.AppendLine($"App Store: {ticket.AppStore}");
            sb.AppendLine($"Feedback ID: {ticket.FeedbackIdApp}");

            sb.AppendLine();

            sb.AppendLine("Feedback:");
            sb.AppendLine(ticket.FeedbackApp);

            sb.AppendLine();

            sb.AppendLine("Original Comment:");
            sb.AppendLine(ticket.Comment);

            return sb.ToString();
        }

        public void Dispose()
        {
            _tokenSemaphore.Dispose();
        }
    }

    [ExcludeFromCodeCoverage]
    public class ZendeskFieldIds
    {
        public long? Subject { get; set; }
        public long? Description { get; set; }
        public long? RatingApp { get; set; }
        public long? FeedbackApp { get; set; }
        public long? FeedbackIdApp { get; set; }
        public long? AppStore { get; set; }
        public long? ResponseApp { get; set; }

        public static ZendeskFieldIds GetFieldIdsForEnvironment(string environment)
        {
            var isProduction = environment.Equals("PROD", StringComparison.OrdinalIgnoreCase) ||
                               environment.Equals("PRODUCTION", StringComparison.OrdinalIgnoreCase);

            return isProduction ? GetProductionFieldIds() : GetSandboxFieldIds();
        }

        private static ZendeskFieldIds GetSandboxFieldIds()
        {
            return new ZendeskFieldIds
            {
                Subject = 360004115339,
                Description = 360004115359,
                RatingApp = 32864764317458,
                FeedbackApp = 32864890088850,
                FeedbackIdApp = 32864817261586,
                AppStore = 32864812572562,
                ResponseApp = 32865263485458
            };
        }

        private static ZendeskFieldIds GetProductionFieldIds()
        {
            return new ZendeskFieldIds
            {
                Subject = 360002090379,
                Description = 360002090399,
                RatingApp = 37979106701842,
                FeedbackApp = 37979094615570,
                FeedbackIdApp = 37979127645714,
                AppStore = 37979179913362,
                ResponseApp = 37979178552210
            };
        }
    }
}
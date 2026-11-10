using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    /// <summary>
    /// Parses a page of the Zendesk ticket comments API response.
    /// </summary>
    public static class ZendeskCommentParser
    {
        public const string UnknownRole = "unknown";

        /// <summary>
        /// Adds the comments on this page to <paramref name="comments"/> and returns the
        /// next page URL, or null when there are no more pages.
        /// Comments only carry an author_id, so each author's role is looked up in the
        /// sideloaded "users" array (request the page with ?include=users).
        /// </summary>
        public static string ParsePage(string json, List<ZendeskComment> comments)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var rolesByUserId = new Dictionary<long, string>();

            if (root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
            {
                foreach (var user in users.EnumerateArray())
                {
                    if (user.TryGetProperty("id", out var userId) && userId.ValueKind == JsonValueKind.Number
                        && user.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String)
                    {
                        rolesByUserId[userId.GetInt64()] = role.GetString();
                    }
                }
            }

            if (root.TryGetProperty("comments", out var commentsArray) && commentsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var comment in commentsArray.EnumerateArray())
                {
                    comments.Add(new ZendeskComment
                    {
                        Id = comment.GetProperty("id").ToString(),
                        Body = comment.GetProperty("body").GetString() ?? string.Empty,
                        AuthorRole = ResolveAuthorRole(comment, rolesByUserId),
                        Public = comment.TryGetProperty("public", out var isPublic) && isPublic.ValueKind == JsonValueKind.True,
                        CreatedAt = comment.TryGetProperty("created_at", out var createdAt) && createdAt.ValueKind == JsonValueKind.String
                            ? DateTime.Parse(createdAt.GetString()) : DateTime.UtcNow
                    });
                }
            }

            return root.TryGetProperty("next_page", out var nextPage) && nextPage.ValueKind == JsonValueKind.String
                ? nextPage.GetString()
                : null;
        }

        private static string ResolveAuthorRole(JsonElement comment, Dictionary<long, string> rolesByUserId)
        {
            if (comment.TryGetProperty("author_id", out var authorId) && authorId.ValueKind == JsonValueKind.Number
                && rolesByUserId.TryGetValue(authorId.GetInt64(), out var role))
            {
                return role;
            }

            return UnknownRole;
        }
    }
}

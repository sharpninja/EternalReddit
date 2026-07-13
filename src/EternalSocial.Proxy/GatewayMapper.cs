using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;

namespace EternalSocial.Proxy;

/// <summary>
/// Maps the stored route table onto YARP routes/clusters. Prefixes are passed
/// through un-stripped (the funwashad pattern): each downstream absorbs its own
/// prefix via UsePathBase, so no transforms are needed here.
/// </summary>
public static class GatewayMapper
{
    /// <summary>Path prefixes the proxy itself owns; never routable or assignable.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/login", "/logout", "/signin-oidc", "/admin", "/_gateway", "/health", "/denied", "/favicon.ico"
    };

    private static readonly Regex PrefixShape = new("^/[a-z0-9-]{1,32}$", RegexOptions.Compiled);

    public static bool IsValidPrefix(string prefix)
        => PrefixShape.IsMatch(prefix) && !Reserved.Contains(prefix);

    public static bool IsValidUpstream(string upstream)
        => upstream.Length == 0
           || (Uri.TryCreate(upstream, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Map(IEnumerable<ProxyRoute> routes)
    {
        var yarpRoutes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var route in routes.Where(r => r.Enabled && r.Upstream.Length > 0))
        {
            var id = route.Prefix.Trim('/');
            yarpRoutes.Add(new RouteConfig
            {
                RouteId = id,
                ClusterId = id,
                Order = 10,
                Match = new RouteMatch { Path = $"{route.Prefix}/{{**catch-all}}" }
            });
            clusters.Add(new ClusterConfig
            {
                ClusterId = id,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["default"] = new() { Address = route.Upstream.EndsWith('/') ? route.Upstream : route.Upstream + "/" }
                }
            });
        }

        return (yarpRoutes, clusters);
    }
}

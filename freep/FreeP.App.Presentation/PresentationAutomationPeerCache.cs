namespace FreeP.App.Compositor;

/// <summary>
/// Keeps renderer-owned automation peers aligned with the current portable shape projection.
/// Native renderers still create peers and translate them to framework provider types.
/// </summary>
public static class PresentationAutomationPeerCache
{
    public static IReadOnlyList<TPeer> Synchronize<TPeer>(
        IReadOnlyList<PresentationCanvasAutomationDescriptor> descriptors,
        IDictionary<uint, TPeer> peers,
        Func<uint, TPeer> createPeer)
        where TPeer : class
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(createPeer);

        var livePeers = new List<TPeer>(descriptors.Count);
        var liveIds = new HashSet<uint>();
        foreach (var descriptor in descriptors)
        {
            var shapeId = descriptor.ShapeId
                ?? throw new ArgumentException(
                    "Automation peer projections must identify a shape.",
                    nameof(descriptors));
            liveIds.Add(shapeId);
            livePeers.Add(GetOrCreate(peers, shapeId, createPeer));
        }

        foreach (var staleId in peers.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            peers.Remove(staleId);

        return livePeers;
    }

    public static TPeer GetOrCreate<TPeer>(
        IDictionary<uint, TPeer> peers,
        uint shapeId,
        Func<uint, TPeer> createPeer)
        where TPeer : class
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(createPeer);

        if (peers.TryGetValue(shapeId, out var peer))
            return peer;

        peer = createPeer(shapeId)
            ?? throw new InvalidOperationException("The automation peer factory returned null.");
        peers.Add(shapeId, peer);
        return peer;
    }
}

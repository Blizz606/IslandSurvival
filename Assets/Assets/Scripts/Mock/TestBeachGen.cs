// Keep this component and its .meta GUID so existing scenes and generator
// references retain their Inspector assignments. All beach logic is shared.
public class TestBeachGen : BeachGenerator
{
    protected override bool UsesRotatedTiles => true;

    private void Reset()
    {
        beachWidth = 1;
    }
}

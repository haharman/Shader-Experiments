public readonly struct LidarPoint
{
    public readonly float Distance; // mm
    public readonly float Angle; // degree
    public readonly float Quality;
    
    public LidarPoint(float distance, float angle, float quality)
    {
        Distance = distance;
        Angle = angle;
        Quality = quality;
    }
}
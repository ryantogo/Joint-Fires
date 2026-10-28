namespace JointFires.Sensors
{
    // How a friendly unit learned about a target. Sent on the wire as a byte; append only.
    public enum SensorKind : byte
    {
        // A radar (aircraft, SAM, ship or ground radar).
        Radar = 1,
        // Any non-radar TargetDetector: eyes, IRST, optics.
        Visual = 2,
        // A missile's own seeker.
        MissileSeeker = 3,
        // A laser designator painting it.
        Laser = 4,
        // Passive radar-warning location of an emitter.
        RadarWarning = 5,
        // Missile approach warning.
        MissileWarning = 6,
        // A report from a remote player's aircraft whose exact sensor the server can't see
        // (laser, RWR or missile warning).
        Reported = 7,
    }
}

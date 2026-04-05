namespace VR.HDRFix.Models
{
    public struct Oklab
    {
        public float L, A, B;

        public Oklab(float l, float a, float b)
        {
            L = l;
            A = a;
            B = b;
        }
    }
}
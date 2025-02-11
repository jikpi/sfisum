using sfisum.Utils;

namespace sfisum.FileRep;

internal readonly struct HashString
{
    public string Hash { get; }

    public HashString(string hashText)
    {
        if (string.IsNullOrEmpty(hashText))
        {
            throw new LocalException("Hash string cannot be null or empty");
        }


        if (hashText.Length != 32)
        {
            throw new LocalException("Hash string must be 32 characters long");
        }

        Hash = hashText;
    }
}
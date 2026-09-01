namespace EasyDotnet.ContainerTests;

public static class CommandArguments
{
  public static int IndexOfSequence(List<string> args, params string[] sequence)
  {
    for (var i = 0; i + sequence.Length <= args.Count; i++)
    {
      if (args.Skip(i).Take(sequence.Length).SequenceEqual(sequence)) return i;
    }

    return -1;
  }
}
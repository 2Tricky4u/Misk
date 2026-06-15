namespace Misk.Domain;

/// <summary>Result of validating/attempting a command. Carries a human-readable reason on failure.</summary>
public readonly struct CommandResult
{
	public bool Ok { get; }
	public string Error { get; }

	private CommandResult( bool ok, string error )
	{
		Ok = ok;
		Error = error;
	}

	public static CommandResult Success { get; } = new CommandResult( true, null );
	public static CommandResult Fail( string error ) => new CommandResult( false, error );
}

/// <summary>
/// A player action. Validate is a pure check (no mutation); Execute assumes Validate
/// already passed. The Command pattern gives every action a uniform shape — easy to route
/// over the network, log, replay, or later drive from AI.
/// </summary>
public interface IGameCommand
{
	/// <summary>The player attempting the action (must match the current player).</summary>
	string PlayerId { get; }

	CommandResult Validate( GameContext ctx );
	void Execute( GameContext ctx );
}

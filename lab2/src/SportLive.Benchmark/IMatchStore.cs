namespace SportLive.Benchmark;

public sealed record GoalOp(int MatchId, int Seq, int Minute, bool IsHome, int TeamId, int PlayerId, string PlayerName, int AssistId);

public interface IMatchStore
{
    string Name { get; }

    void Reset(DataGenerator generator);
    void Load(IReadOnlyList<Match> matches);
    void CreateIndexes();
    double StorageMb();

    Match? GetMatchCard(int matchId);
    int GetTeamMatches(int teamId, DateTime from, DateTime to);
    IReadOnlyList<(int PlayerId, int Goals)> GetTopScorers(int tournamentId);
    long CountFinishedInCity(string city);

    void AppendGoal(GoalOp op);
    void UpdatePossession(int matchId, decimal home);
    void InsertBatch(IReadOnlyList<Match> matches);
}

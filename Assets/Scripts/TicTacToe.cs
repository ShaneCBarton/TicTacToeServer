// TicTacToe game for server side

public class TicTacToe
{
    public enum Player { None, X, O }

    private Player[] board = new Player[9];
    private Player currentPlayer = Player.X;

    public TicTacToe()
    {
        for (int i = 0; i < board.Length; i++)
        {
            board[i] = Player.None;
        }
    }

    public bool MakeMove(int position)
    {
        if (position < 0 || position >= 9 || board[position] != Player.None)
            return false;

        board[position] = currentPlayer;

        currentPlayer = (currentPlayer == Player.X) ? Player.O : Player.X;

        return true;
    }

    public Player GetWinner()
    {
        int[,] winPatterns = new int[,]
        {
            { 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 },
            { 0, 3, 6 }, { 1, 4, 7 }, { 2, 5, 8 },
            { 0, 4, 8 }, { 2, 4, 6 }
        };

        for (int i = 0; i < 8; i++)
        {
            int a = winPatterns[i, 0];
            int b = winPatterns[i, 1];
            int c = winPatterns[i, 2];

            if (board[a] != Player.None && board[a] == board[b] && board[a] == board[c])
            {
                return board[a];
            }
        }

        return Player.None;
    }

    public bool IsBoardFull()
    {
        foreach (var spot in board)
        {
            if (spot == Player.None)
                return false;
        }
        return true;
    }

    public Player[] GetBoardState()
    {
        return board;
    }

    public Player GetCurrentPlayer()
    {
        return currentPlayer;
    }
}

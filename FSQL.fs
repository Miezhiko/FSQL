open System
open System.IO
open System.Text
open System.Drawing
open System.Windows.Forms

open System.Drawing.Drawing2D

open System.Runtime.InteropServices
open System.Text.RegularExpressions

open System.Data
open System.Data.SqlClient

open MetroFramework.Forms
open MetroFramework.Controls

let version = "1.0.0"
let fsql (lines : seq<string>) = 
  String.Join(Environment.NewLine,
    [for line in lines do
      let nsplitted = [for s in line.Split(' ','\r','\n','\t',';') do
                if s <> "" && not <| String.IsNullOrEmpty(s) then
                  yield s]
      let newline = ref line
      if nsplitted.Length > 1 then
        for word in nsplitted do
          let repl =
            match word.ToLower() with
            | "{" -> "begin"
            | "}" -> "end"
            | "!" -> "select"
            | "~" -> "from"
            | "var" -> "declare"
            | _ -> ""
          if not <| String.IsNullOrEmpty(repl)
            then newline:= (!newline).Replace(word, repl)
      yield !newline ])

module Lock =
  [<DllImport(@"User32", CharSet = CharSet.Ansi, SetLastError = false, ExactSpelling = true)>]
  extern void LockWindowUpdate(int hWnd)
        
type RichTextBoxWithSqlHighlighting() = 
  inherit System.Windows.Forms.RichTextBox()
  override x.OnTextChanged(e : System.EventArgs) =
    base.OnTextChanged(e); x.ColorTheKeyWords()
  member x.ColorTheKeyWords() =
    let color(m : Match, color : Color) =
      x.SelectionStart  <- m.Index
      x.SelectionLength   <- m.Length
      x.SelectionColor  <- color
    let hl s c =
      Regex.Matches(x.Text, "\\b" + s + "\\b", RegexOptions.IgnoreCase) |> fun mx ->
        for m in mx do if (m.Success) then color(m,c)
    let SelectionAt = x.SelectionStart
    Lock.LockWindowUpdate(x.Handle.ToInt32())
    hl "(\*)|(!)|(~)|(,)|(@)" Color.Blue
    hl "(\()|(\))" Color.DarkGray

    let hlW (w : string) (s : string) c =
      let mutable docolor = false
      for mtch in s.Split('(','|',')') do
        if not docolor && mtch <> "" && w.ToLower() = mtch then 
          docolor <- true
          Regex.Matches(x.Text, "\\b" + s + "\\b", RegexOptions.IgnoreCase)
          |> fun mx -> for m in mx do if (m.Success) then color(m,c)
    for word in x.Text.Split( ' ', '*', '!', '~', ',', '@'
                , '(', ')', '[', ']'
                , '\n' ,'\r' ,'\t') do
      hlW word "(let)" Color.Blue
      hlW word "(and)|(or)|(not)" Color.DarkGreen
      hlW word "(avg)|(abs)|(max)|(min)" Color.DarkRed
      hlW word "(select)|(where)|(from)|(top)|(order)|(group)|(by)|(as)|(null)|(insert)|(exec)|(into)" Color.Blue
      hlW word "(desc)|(asc)" Color.Brown
      hlW word "(join)|(left)|(inner)|(outer)|(right)|(on)" Color.Red
      hlW word "(case)|(when)|(then)|(else)|(end)|(if)|(begin)" Color.Teal
      hlW word "(cast)|(nvarchar)|(bit)|(datetime)|(int)|(table)" Color.BlueViolet
      hlW word "(datepart)" Color.DarkOrange

    Regex.Matches(x.Text, "\[(.*?)\]", RegexOptions.IgnoreCase) |> fun mx ->
      for m in mx do if (m.Success) then color(m,Color.DarkBlue)
      
    x.SelectionStart  <- SelectionAt
    x.SelectionLength   <- 0
    x.SelectionColor  <- Color.Black
    Lock.LockWindowUpdate(0)

let datasourceConf = "datasource.conf"
let lastQueryConf  = "lastQery.fsql"

let form = new MetroForm()
form.Width  <- 800
form.Height <- 750
form.Text   <- "Fast SQL"
form.Font   <- new Font( "Lucida Console"
             , 12.0f
             , FontStyle.Regular,GraphicsUnit.Point )

form.FormBorderStyle <- FormBorderStyle.FixedDialog
form.MaximizeBox     <- false
form.MinimizeBox     <- false

let l2 = new MetroLabel();
l2.Location <- Point(20,325); l2.Text <- "Output"
let r0 = new RichTextBox();
r0.Location <- Point(5, 10); r0.Size <- Size(form.Width - 30, 50); 

r0.Text <- if File.Exists datasourceConf
        then ReadFileAsString datasourceConf
        else @"Data Source=(LocalDb)\Initial Catalog=db;Integrated Security=True"

let ddp = new MetroPanel();
ddp.Location <- Point(5, 23); ddp.Size <- Size(form.Width - 10, 70); 
ddp.Controls.Add r0

let r1 = new RichTextBoxWithSqlHighlighting();
r1.Location <- Point(10, 100); r1.Size <- Size(770, 200); 

r1.Text <- if File.Exists lastQueryConf
        then ReadFileAsString lastQueryConf
        else "! [FirstName], [LastName] ~ [Table]"

let r2 = new RichTextBox();
r2.Location <- Point(10, 350); r2.Size <- Size(770, 300)

let b1 = new MetroButton();
b1.Location <- Point(40, 680); b1.Size <- Size(150, 50); b1.Text <- "Exit"

let b2 = new MetroButton();
b2.Location <- Point(580, 680); b2.Size <- Size(150, 50); b2.Text <- "Go"

let gv = new DataGridView();
gv.Location <- Point(10, 350); gv.Size <- Size(770, 300); gv.Visible <- false

let menuStrip = new ContextMenuStrip();
let fileMenu = new ToolStripMenuItem();
fileMenu.Text <- "File";
let aboutMenu = new ToolStripMenuItem();
aboutMenu.Text <- "About";

let openM = new ToolStripMenuItem();
openM.Text <- "Open";

let saveM = new ToolStripMenuItem();
saveM.Text <- "Save";

let exitM = new ToolStripMenuItem();
exitM.Text <- "Exit";

fileMenu.DropDownItems.Add openM
fileMenu.DropDownItems.Add saveM
fileMenu.DropDownItems.Add exitM

menuStrip.Items.Add fileMenu
menuStrip.Items.Add aboutMenu

form.ContextMenuStrip <- menuStrip

let runQuery () =
  let cmd = fsql r1.Lines
  try
    use conn  = new SqlConnection( r0.Text )
    use command = new SqlCommand(cmd, conn)
    conn.Open()
    if cmd.ToLower().Contains("select")
      then
        let dt = new DataTable()
        use adapter = new SqlDataAdapter(command)
        
        adapter.Fill(dt) |> ignore
        
        WriteToFile false datasourceConf r0.Lines
        WriteToFile false lastQueryConf r1.Lines
        
        r2.Visible <- false
        gv.Visible <- true
        
        gv.DataSource <- dt
        gv.AutoResizeColumns()
      else
        command.ExecuteNonQuery() |> ignore
        r2.Text <- r2.Text + "OK" + Environment.NewLine
    conn.Close()
  with
    | exn -> r2.Visible <- true
         gv.Visible <- false
         r2.Text <- r2.Text
                + "Exception:\n"
                + exn.Message + Environment.NewLine

aboutMenu.Click.Add (fun _ -> 
  MessageBox.Show("FSQL v." + version) |> ignore
)

openM.Click.Add (fun _ -> 
  let ofd = new OpenFileDialog()
  let dr = ofd.ShowDialog()
  if dr = DialogResult.OK then
    if File.Exists ofd.FileName then
      r1.Text <- ReadFileAsString ofd.FileName
)
  
saveM.Click.Add (fun _ -> 
  let sfd=new SaveFileDialog()
  sfd.FileName  <- "unknown.fsql"
  sfd.Filter    <- "FSQL (*.fsq)|*.fsq|All files (*.*)|*.*"
  let dr = sfd.ShowDialog()
  if dr = DialogResult.OK then
    WriteToFile false sfd.FileName r1.Lines
)

exitM.Click.Add (fun _ -> ignore <| form.Close())
b1.Click.Add    (fun _ -> ignore <| form.Close())
b2.Click.Add    (fun _ -> runQuery())
form.Shown.Add  (fun _ -> r1.ColorTheKeyWords())

form.Controls.AddRange [|ddp; l2; b1; b2; r1; r2; gv|]
Application.Run(form)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Kruisjassen48
{
    public class MainForm : Form
    {
        int potsLeftWithTrump = 0;
        bool haveTrumpCycle = false;
        Rectangle invalidOverlayRect = Rectangle.Empty;
        string invalidReason = "";

        // ===== Model =====
        enum Suit { Clubs, Diamonds, Hearts, Spades }
        static readonly string[] SuitGlyph = { "♣", "♦", "♥", "♠" };
        static readonly bool[] SuitIsRed = { false, true, true, false };
        static readonly string[] RankOrder = { "7","8","9","10","B","V","H","A" };

        class Card { public Suit Suit; public string Rank; public override string ToString(){ return Rank + SuitGlyph[(int)Suit]; } }
        class Trick { public readonly List<Card> Cards=new List<Card>(); public readonly List<int> Players=new List<int>(); public Suit Lead; public int Leader; public Trick(int l){Leader=l;} }

        readonly Dictionary<string,int> NonTrumpOrd = new Dictionary<string,int>{{"7",0},{"8",1},{"9",2},{"10",3},{"B",4},{"V",5},{"H",6},{"A",7}};
        readonly Dictionary<string,int> TrumpOrd    = new Dictionary<string,int>{{"7",0},{"8",1},{"10",2},{"V",3},{"H",4},{"A",5},{"9",6},{"B",7}};
        readonly Dictionary<string,int> NonTrumpPts = new Dictionary<string,int>{{"A",11},{"H",3},{"V",2},{"B",1},{"10",10},{"9",0},{"8",0},{"7",0}};
        readonly Dictionary<string,int> TrumpPts    = new Dictionary<string,int>{{"A",11},{"H",3},{"V",2},{"B",20},{"10",10},{"9",14},{"8",0},{"7",0}};

        class GS {
            public List<Card>[] Hands = new List<Card>[] { new List<Card>(), new List<Card>(), new List<Card>(), new List<Card>() };
            public Suit Trump; public Card TrumpShown;
            public int Dealer=0, ToPlay=0, TrickIndex=0, LastWinner=-1;
            public Trick Current = new Trick(0);
            public List<Card> CapturedOur = new List<Card>();      // jouw team (Zuid+Noord)
            public List<Card> CapturedOther = new List<Card>();    // andere team (West+Oost)
            public bool WaitingNext=false;
            public bool WaitingCollect=false; public int PendingWinner=-1;
            public Random Rnd=new Random();
        }
        readonly GS gs = new GS();

        // ===== UI =====
        readonly Panel table = new Panel(){ Dock=DockStyle.Fill, BackColor=Color.FromArgb(0x0f,0x51,0x32) };
        readonly FlowLayoutPanel hand = new FlowLayoutPanel{ Dock=DockStyle.Bottom, Height=160, AutoScroll=true, BackColor=Color.FromArgb(0x0a,0x2c,0x1f)};
        readonly Timer aiTimer = new Timer(){ Interval=500 };
        readonly Queue<int> aiQueue = new Queue<int>();
        Button[] handButtons = new Button[0];

        // Invalid overlay + reason text
        DateTime invalidOverlayUntil = DateTime.MinValue;
        Timer invalidOverlayTimer;
        Label lblInvalid;

        bool showOurExpanded=false, showOtherExpanded=false;
        readonly Button btnVolgende = new Button(){ Text="Volgende potje", Enabled=false };
        readonly Button btnRegels = new Button(){ Text="Regels" };
        readonly Button btnPunten = new Button(){ Text="Punten" };

        public MainForm()
        {
            Text = "Kruisjassen – .NET Framework 4.8 (v4.7a)";
            Width=1100; Height=740; BackColor=Color.FromArgb(0x0a,0x2c,0x1f);
            Controls.Add(table);
            Controls.Add(hand);

            var top = new FlowLayoutPanel{ Dock=DockStyle.Top, Height=40, BackColor=Color.FromArgb(0x0a,0x2c,0x1f)};
            foreach(var b in new[]{btnRegels, btnPunten, btnVolgende})
            {
                b.FlatStyle = FlatStyle.Standard;
                b.UseVisualStyleBackColor = false;
                b.BackColor = Color.White; // wit
                b.ForeColor = Color.Black;
                b.Margin = new Padding(8,6,0,6);
            }
            btnRegels.Click += (s,e)=>ShowRules();
            btnPunten.Click += (s,e)=>ShowPointsDialog();
            btnVolgende.Click += (s,e)=>{ if(gs.WaitingNext){ gs.Dealer=(gs.Dealer+1)%4; StartPot(); } };
            top.Controls.Add(btnRegels); top.Controls.Add(btnPunten); top.Controls.Add(btnVolgende);
            Controls.Add(top);

            lblInvalid = new Label{
                Dock=DockStyle.Top, Height=22, ForeColor=Color.Red, TextAlign=ContentAlignment.MiddleLeft,
                BackColor=Color.White, Visible=false
            };
            Controls.Add(lblInvalid);
            lblInvalid.BringToFront();

            table.Paint += Table_Paint;
            table.MouseClick += Table_MouseClick;

            aiTimer.Tick += AiTimer_Tick;

            invalidOverlayTimer = new Timer(); invalidOverlayTimer.Interval = 30;
            invalidOverlayTimer.Tick += InvalidOverlayTimer_Tick;

            StartPot();
        }

        // ======= UI: events =======
        void AiTimer_Tick(object sender, EventArgs e)
        {
            if(aiQueue.Count==0){ aiTimer.Stop(); return; }
            int p = aiQueue.Dequeue();
            if(gs.Hands[p].Count>0){ Play(p, ChooseAI(p), null); }
            if(aiQueue.Count==0) aiTimer.Stop();
        }

        void InvalidOverlayTimer_Tick(object sender, EventArgs e)
        {
            if(DateTime.Now >= invalidOverlayUntil){ invalidOverlayTimer.Stop(); invalidOverlayRect = Rectangle.Empty; table.Invalidate(); }
            else table.Invalidate();
        }

        void Table_MouseClick(object sender, MouseEventArgs e)
        {
            // elke klik wist de redenstekst
            invalidReason=""; lblInvalid.Visible=false; table.Invalidate();

            if(RectCenter().Contains(e.Location) && gs.WaitingCollect){ CollectCurrentTrick(); return; }
            if(GetOurPileRect().Contains(e.Location)) { showOurExpanded = !showOurExpanded; RenderAll(); return; }
            if(GetOtherPileRect().Contains(e.Location)) { showOtherExpanded = !showOtherExpanded; RenderAll(); return; }
        }

        // ======= Top dialogs =======
        void ShowRules()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Spelverloop en zettenregels (zonder punten):");
            sb.AppendLine("• 4 spelers. Zuid = jij. 32 kaarten (7..A).");
            sb.AppendLine("• De deler toont alleen het troefsymbool (geen kaart uit het dek).");
            sb.AppendLine("• Uitkomen begint links van de deler; met de klok mee.");
            sb.AppendLine("• Van soort bedienen is verplicht, óf je legt troef.");
            sb.AppendLine("• Uitzondering: als jouw maat in de lopende slag al troef heeft gelegd, mag je vrij kiezen (je hoeft niet te bedienen of troef te leggen).");
            sb.AppendLine("• Kun je niet bedienen en ben je niet vrijgesteld door troef van je maat, dan moet je troef spelen. Ondertroeven is toegestaan.");
            sb.AppendLine("• Hoogte niet-troef (hoog→laag): A, H, V, B, 10, 9, 8, 7.");
            sb.AppendLine("• Hoogte troef (hoog→laag): B, 9, A, H, V, 10, 8, 7.");
            sb.AppendLine("• De winnaar van de slag komt de volgende slag uit.");
            sb.AppendLine("• Boer-plicht: als troef is gevraagd en je kunt bedienen, en je maat de slag niet leidt, moet je de troefboer spelen als je die bezit.");
            MessageBox.Show(this, sb.ToString(), "Kruisjassen – Regels", MessageBoxButtons.OK, MessageBoxIcon.Information);
        
        }

        void ShowPointsDialog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Puntenwaardes per kaart:");
            sb.AppendLine("");
            sb.AppendLine("Niet-troef:   A=11, H=3, V=2, B=1, 10=10, 9=0, 8=0, 7=0");
            sb.AppendLine("Troef:        B=20, 9=14, A=11, H=3, V=2, 10=10, 8=0, 7=0");
                        sb.AppendLine("Opmerking: de troef negen wordt de \"Nel\" genoemd.");
sb.AppendLine("");
            sb.AppendLine("Puntentelling:");
            sb.AppendLine("• Totaal per pot is 146 (incl. +5 laatste slag).");
            sb.AppendLine("• Alle slagen door één team = ‘kapot gespeeld’: +45 extra voor dat team.");
            MessageBox.Show(this, sb.ToString(), "Kruisjassen – Punten", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowEightPotPopup()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Einde van 8 potjes.");
            sb.AppendLine("Tel de punten op en noteer de tussenstand.");
            sb.AppendLine("Klik op OK om door te gaan met een nieuwe troef.");
            MessageBox.Show(this, sb.ToString(), "Kruisjassen – 8 potjes afgerond", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    


        // ======= Flow =======
        void StartPot()
        {
            Deal();
            gs.TrickIndex=0; gs.LastWinner=-1;
            gs.CapturedOur.Clear(); gs.CapturedOther.Clear();
            gs.Current=new Trick((gs.Dealer+1)%4); gs.ToPlay=gs.Current.Leader;
            gs.WaitingNext=false; btnVolgende.Enabled=false; gs.WaitingCollect=false; gs.PendingWinner=-1;
            invalidReason=""; lblInvalid.Visible=false;
            RenderAll();
            MaybeAISequence();
        }

        void Deal()
        {
            var deck=new List<Card>();
            foreach(Suit s in Enum.GetValues(typeof(Suit)))
                foreach(var r in RankOrder) deck.Add(new Card{Suit=s,Rank=r});

            // Fisher–Yates shuffle
            for(int i=deck.Count-1;i>0;i--){
                int j = gs.Rnd.Next(i+1);
                var t=deck[i]; deck[i]=deck[j]; deck[j]=t;
            }

            for(int i=0;i<4;i++){ gs.Hands[i].Clear(); }
            for(int i=0;i<32;i++) gs.Hands[i%4].Add(deck[i]);
            for(int i=0;i<4;i++) gs.Hands[i]=gs.Hands[i].OrderBy(c=> ((int)c.Suit)*100 + Array.IndexOf(RankOrder,c.Rank)).ToList();

            // Trump suit: same for 8 pots. Show popup before changing (after a full cycle)
            if(potsLeftWithTrump<=0){
                if(haveTrumpCycle) ShowEightPotPopup();
                gs.Trump = (Suit)gs.Rnd.Next(4);
                gs.TrumpShown = null;
                potsLeftWithTrump = 8;
                haveTrumpCycle = true;
            }
            potsLeftWithTrump--;
        
        }

        // ======= Layout helpers =======
        Rectangle RectNorth(){ return new Rectangle(220, 10, table.Width-440, 120); }
        Rectangle RectSouth(){ return new Rectangle(220, table.Height-130, table.Width-440, 120); }
        Rectangle RectWest(){ return new Rectangle(10, (table.Height-160)/2, 200, 160); }
        Rectangle RectEast(){ return new Rectangle(table.Width-210, (table.Height-160)/2, 200, 160); }
        Rectangle RectCenter(){ return new Rectangle((table.Width-240)/2, (table.Height-200)/2, 240, 200); }
        Rectangle RectPileOther(){ return new Rectangle(20, 20, 180, 140); }                            // linksboven
        Rectangle RectPileOurBase(){ return new Rectangle(table.Width-200, table.Height-150, 180, 140); }  // rechtsonder
        Rectangle RectTrump(){ return new Rectangle(10, table.Height-110, 60, 90); }                      // linksonder

        void RenderAll(){ RenderSouthHand(); table.Invalidate(); }

        void RenderSouthHand()
        {
            hand.Controls.Clear();
            var list = gs.Hands[0];
            handButtons = new Button[list.Count];
            bool my = gs.ToPlay==0 && !gs.WaitingNext && !gs.WaitingCollect;
            for(int i=0;i<list.Count;i++)
            {
                var c = list[i];
                var b = new Button{ Width=80, Height=120, Tag=i, FlatStyle=FlatStyle.Flat, Text="" };
                b.FlatAppearance.BorderSize=0; b.BackgroundImageLayout=ImageLayout.Stretch;
                var bmp = new Bitmap(80,120);
                using(var g=Graphics.FromImage(bmp)){ DrawCard(g, c, new Rectangle(0,0,80,120), false); }
                b.BackgroundImage=bmp;
                string _; // ignore reason here for enabling
                b.Enabled = my && IsLegal(0,c, out _);
                int idx=i; var btnRef=b;
                b.Click += (s,e)=>{ Play(0, idx, btnRef); };
                hand.Controls.Add(b);
                handButtons[i]=b;
            }
        }

        // ======= Painting =======
        void Table_Paint(object sender, PaintEventArgs e)
        {
            var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            DrawSeatAreas(g);
            DrawTrick(g);
            DrawTrump(g);      // eerst troef…
            DrawCaptured(g);   // …dan stapels zodat 'jouw team' eroverheen kan tekenen

            // Invalid overlay bovenop
            if(!invalidOverlayRect.IsEmpty){
                using(var br = new SolidBrush(Color.FromArgb(160, 200, 40, 40)))
                    g.FillRectangle(br, invalidOverlayRect);
                using(var pen = new Pen(Color.FromArgb(240, 255, 80, 80), 4)){
                    g.DrawRectangle(pen, invalidOverlayRect);
                    g.DrawLine(pen, invalidOverlayRect.Left, invalidOverlayRect.Top, invalidOverlayRect.Right, invalidOverlayRect.Bottom);
                    g.DrawLine(pen, invalidOverlayRect.Right, invalidOverlayRect.Top, invalidOverlayRect.Left, invalidOverlayRect.Bottom);
                }
            }
        }

        void DrawSeatAreas(Graphics g)
        {
            using(var pen=new Pen(Color.FromArgb(120,200,200,200)))
            { g.DrawRectangle(pen, RectNorth()); g.DrawRectangle(pen, RectSouth()); g.DrawRectangle(pen, RectWest()); g.DrawRectangle(pen, RectEast()); }
            DrawPileBacks(g, RectWest(), 1);
            DrawPileBacks(g, RectNorth(), 2);
            DrawPileBacks(g, RectEast(), 3);
            DrawPileBacks(g, RectSouth(), 0);
        }

        void DrawPileBacks(Graphics g, Rectangle area, int player)
        {
            int n = gs.Hands[player].Count;
            int px=area.X+6, py=area.Y+6;
            for(int i=0;i<n;i++)
            { var r=new Rectangle(px,py,18,28); DrawBack(g,r); px+=20; if(px+18>area.Right){ px=area.X+6; py+=30; } }
        }

        void DrawTrick(Graphics g)
        {
            var c = RectCenter();
            int cw=80, ch=120;
            for(int i=0;i<gs.Current.Cards.Count; i++)
            {
                var card = gs.Current.Cards[i];
                int p = gs.Current.Players[i];
                Rectangle r;
                if(p==0) r = new Rectangle(c.X + (c.Width-cw)/2, c.Y + c.Height - ch, cw, ch);
                else if(p==2) r = new Rectangle(c.X + (c.Width-cw)/2, c.Y, cw, ch);
                else if(p==1) r = new Rectangle(c.X, c.Y + (c.Height-ch)/2, cw, ch);
                else r = new Rectangle(c.Right - cw, c.Y + (c.Height-ch)/2, cw, ch);
                DrawCard(g, card, r, false);
            }
        }

        void DrawCaptured(Graphics g)
        {
            DrawCapturedPile(g, GetOurPileRect(), gs.CapturedOur, "jouw team", showOurExpanded, true);
            DrawCapturedPile(g, GetOtherPileRect(), gs.CapturedOther, "andere team", showOtherExpanded, false);
        }

        Rectangle GetOurPileRect()
        {
            if(!showOurExpanded) return RectPileOurBase();
            int cellW=52, cellH=78;
            int cols = Math.Max(1, (table.Width-40)/cellW);
            int rows = Math.Max(1, (int)Math.Ceiling(gs.CapturedOur.Count/(double)cols));
            int width = Math.Min(cols*cellW, table.Width-40);
            cols = Math.Max(1, width/cellW);
            rows = Math.Max(1, (int)Math.Ceiling(gs.CapturedOur.Count/(double)cols));
            int height = rows*cellH;
            int x = table.Width - 20 - width;
            int y = Math.Max(10, table.Height - 10 - height);
            return new Rectangle(x, y, width, height);
        }

        Rectangle GetOtherPileRect()
        {
            if(!showOtherExpanded) return RectPileOther();
            int cellW=52, cellH=78;
            int cols = Math.Max(1, (table.Width-40)/cellW);
            int rows = Math.Max(1, (int)Math.Ceiling(gs.CapturedOther.Count/(double)cols));
            int width = Math.Min(cols*cellW, table.Width-40);
            cols = Math.Max(1, width/cellW);
            rows = Math.Max(1, (int)Math.Ceiling(gs.CapturedOther.Count/(double)cols));
            int height = rows*cellH;
            int x = 20;
            int y = 20;
            if(height > table.Height-40){
                cols = Math.Max(1, (table.Width-40)/cellW);
                rows = Math.Max(1, (int)Math.Ceiling(gs.CapturedOther.Count/(double)cols));
                height = Math.Min(rows*cellH, table.Height-40);
            }
            if(height > table.Height-40){ height = table.Height-40; }
            if(width > table.Width-40){ width = table.Width-40; }
            return new Rectangle(x, y, width, height);
        }

        void DrawCapturedPile(Graphics g, Rectangle area, List<Card> cards, string title, bool expanded, bool ourTeam)
        {
            using(var f = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)){ g.DrawString(title, f, Brushes.White, area.X, area.Y-14); }

            int tricks = cards.Count / 4; // slagen
            if(!expanded)
            {
                int px=area.X+4, py=area.Y+4;
                for(int i=0;i<tricks;i++)
                {
                    var r=new Rectangle(px,py,50,75); DrawBack(g, r);
                    px += 12; py += 4;
                }
                using(var f2 = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold))
                {
                    g.DrawString(tricks + " slagen", f2, Brushes.White, area.X+4, area.Bottom+2);
                }
                using(var pen=new Pen(Color.FromArgb(180,255,255,255), 1)) g.DrawRectangle(pen, area);
                return;
            }

            // Expanded: alle kaarten zichtbaar in grid binnen area.
            int cellW=52, cellH=78;
            int cols = Math.Max(1, area.Width / cellW);
            int rows = Math.Max(1, (int)Math.Ceiling(cards.Count/(double)cols));
            int x0 = area.X+4;
            int y0 = area.Y+4;
            int cx=x0, cy=y0;
            int shown=0;
            for(int i=0;i<cards.Count;i++){
                var r = new Rectangle(cx, cy, 50, 75);
                DrawCard(g, cards[i], r, false);
                shown++;
                cx += cellW;
                if((shown % cols)==0){ cx=x0; cy += cellH; }
            }
            using(var pen=new Pen(Color.FromArgb(200,255,255,255), 2)) g.DrawRectangle(pen, area);
        }

        void DrawTrump(Graphics g)
        {
            var r = RectTrump();
            using(var br = new SolidBrush(Color.White))
                g.FillRectangle(br, r);
            using(var pen = new Pen(Color.Gray, 1))
                g.DrawRectangle(pen, r);
            using(var f1=new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold))
            using(var f2=new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)){
                string glyph = SuitGlyph[(int)gs.Trump];
                bool red = SuitIsRed[(int)gs.Trump];
                Brush fore = red? Brushes.Maroon: Brushes.Black;
                var sf=new StringFormat(); sf.Alignment=StringAlignment.Center; sf.LineAlignment=StringAlignment.Center;
                g.DrawString(glyph, f1, fore, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
                g.DrawString("Troef", f2, Brushes.White, r.X, r.Bottom+2);
            }
        
        }

        // ======= Rules engine =======
        bool Better(Card a,Card b,Suit lead){ bool aT=a.Suit==gs.Trump,bT=b.Suit==gs.Trump; if(aT!=bT) return aT; if(aT) return TrumpOrd[a.Rank]>TrumpOrd[b.Rank]; bool aL=a.Suit==lead,bL=b.Suit==lead; if(aL!=bL) return aL; if(aL) return NonTrumpOrd[a.Rank]>NonTrumpOrd[b.Rank]; return false; }
        bool IsLegal(int p,Card c, out string reason)
        {
            reason = "";
            if (gs.Current.Cards.Count == 0) return true;

            Suit lead = gs.Current.Lead;
            var handP = gs.Hands[p];
            bool hasLead = handP.Any(k => k.Suit == lead);
            bool hasTrump = handP.Any(k => k.Suit == gs.Trump);

            // Bepaal of maat de slag nu al leidt (ongeacht kaart)
            int mate = (p + 2) % 4;
            bool teammateWinning = false;
            if (gs.Current.Cards.Count > 0)
            {
                int win = gs.Current.Players[0];
                Card best = gs.Current.Cards[0];
                for (int i = 1; i < gs.Current.Cards.Count; i++)
                {
                    if (Better(gs.Current.Cards[i], best, lead)) { best = gs.Current.Cards[i]; win = gs.Current.Players[i]; }
                }
                teammateWinning = (win == mate);
            }

            // --- Boer-plicht bij troef gevraagd en je kunt bedienen, maat leidt NIET ---
            bool leadIsTrump = (lead == gs.Trump);
            if (leadIsTrump && hasLead && !teammateWinning)
            {
                bool hasJack = handP.Any(k => k.Suit == gs.Trump && k.Rank == "B"); // "B" = Boer
                if (hasJack && !(c.Suit == gs.Trump && c.Rank == "B"))
                {
                    reason = "De boer mag niet verzwegen worden bij troef bedienen.";
                    return false;
                }
            }

            // Bedienen mag altijd (mits niet door boer-plicht geblokkeerd)
            if (c.Suit == lead) return true;

            // Je kunt bedienen maar doet dat niet → alleen troef is toegestaan
            if (hasLead)
            {
                if (c.Suit == gs.Trump) return true;
                reason = "Van soort bedienen of troef spelen verplicht.";
                return false;
            }

            // Je kunt niet bedienen
            if (hasTrump)
            {
                // Maat-vrijstelling: als maat de slag leidt ben je vrij
                if (teammateWinning) return true;

                // Onder-troeven toegestaan: elke troef is goed
                if (c.Suit == gs.Trump) return true;

                reason = "Je moet troef spelen.";
                return false;
            }

            // Geen leadkleur en geen troef → vrij weggooien
            return true;

        }
        Card HighestTrump(){ Card best=null; foreach(var k in gs.Current.Cards.Where(cc=>cc.Suit==gs.Trump)) if(best==null||TrumpOrd[k.Rank]>TrumpOrd[best.Rank]) best=k; if(best==null){ best=new Card(); best.Suit=gs.Trump; best.Rank="7"; } return best; }

        // ======= AI & turn flow =======
        int ChooseAI(int p){
            var hand = gs.Hands[p];
            var legal = new List<int>();
            for(int i=0;i<hand.Count;i++){ string _; if(IsLegal(p, hand[i], out _)) legal.Add(i); }
            int partner = (p+2)%4;

            System.Func<int,bool> WouldWin = idx => {
                var tmp = new List<Card>(gs.Current.Cards);
                var tmpPl = new List<int>(gs.Current.Players);
                tmp.Add(hand[idx]); tmpPl.Add(p);
                Suit lead = gs.Current.Lead;
                int win = tmpPl[0];
                Card best = tmp[0];
                for(int k=1;k<tmp.Count;k++){
                    if(Better(tmp[k], best, lead)){ best = tmp[k]; win = tmpPl[k]; }
                }
                return win==p;
            };

            System.Func<int,int> TrickValue = idx => {
                var c = hand[idx];
                int v = (c.Suit==gs.Trump? 100 + TrumpOrd[c.Rank] : NonTrumpOrd[c.Rank]);
                int punten = (c.Rank=="A"?11:(c.Rank=="10"?10:(c.Rank=="H"?3:(c.Rank=="V"?2:(c.Rank=="B"?20:(c.Rank=="9"&&c.Suit==gs.Trump?14:0))))));
                // current winner so far
                int win = gs.Current.Cards.Count>0 ? gs.Current.Players[0] : -1;
                if(gs.Current.Cards.Count>0){
                    Card best = gs.Current.Cards[0];
                    Suit lead = gs.Current.Lead;
                    win = gs.Current.Players[0];
                    for(int k=1;k<gs.Current.Cards.Count;k++){
                        if(Better(gs.Current.Cards[k], best, lead)){ best = gs.Current.Cards[k]; win = gs.Current.Players[k]; }
                    }
                }
                if(win==partner){
                    if(c.Suit==gs.Trump) v += 40;
                    if(punten>0) v += 50;
                }else{
                    if(WouldWin(idx)) v -= 40;
                }
                return v;
            };

            legal.Sort((a,b)=> TrickValue(a).CompareTo(TrickValue(b)));
            return legal[0];
        
        }
        int Val(Card c){ return (c.Suit==gs.Trump? 100 + TrumpOrd[c.Rank] : NonTrumpOrd[c.Rank]); }

        void Play(int p,int idx, Button clicked)
        {
            if(gs.WaitingNext || gs.WaitingCollect) return;
            var handP=gs.Hands[p];
            if(idx<0 || idx>=handP.Count) return;
            var c=handP[idx];
            string why;
            if(!IsLegal(p,c, out why)) { ShowInvalidOverlay(clicked, why); return; }
            invalidReason=""; lblInvalid.Visible=false;
            if(gs.Current.Cards.Count==0) gs.Current.Lead=c.Suit;
            handP.RemoveAt(idx);
            gs.Current.Cards.Add(c); gs.Current.Players.Add(p);
            Advance();
        }

        void ShowInvalidOverlay(Button clicked, string why)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ongeldige zet:");
            sb.AppendLine(why);
            sb.AppendLine("Klik op OK om verder te gaan.");
            MessageBox.Show(this, sb.ToString(), "Kruisjassen – Ongeldige zet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        
        }

        void Advance()
        {
            if(gs.Current.Cards.Count==4)
            {
                int bestIdx=0; var best=gs.Current.Cards[0];
                for(int i=1;i<4;i++) if(Better(gs.Current.Cards[i],best,gs.Current.Lead)){ best=gs.Current.Cards[i]; bestIdx=i; }
                int winner=gs.Current.Players[bestIdx];
                gs.PendingWinner=winner; gs.WaitingCollect=true;
                RenderAll();
                return;
            }
            else
            {
                gs.ToPlay=(gs.ToPlay+1)%4;
            }
            RenderAll();
            MaybeAISequence();
        }

        void CollectCurrentTrick()
        {
            if(!gs.WaitingCollect) return;
            int winner=gs.PendingWinner;
            if(winner%2==0) gs.CapturedOur.AddRange(gs.Current.Cards);
            else gs.CapturedOther.AddRange(gs.Current.Cards);
            gs.TrickIndex++; gs.LastWinner=winner;
            gs.Current=new Trick(winner); gs.ToPlay=winner;
            gs.WaitingCollect=false; gs.PendingWinner=-1;

            if(gs.TrickIndex==8){
                gs.WaitingNext=true; btnVolgende.Enabled=true;
                MessageBox.Show(this,"Potje klaar. Tel nu de punten handmatig aan de stapels.\nKlik daarna op 'Volgende potje'.","Einde potje",MessageBoxButtons.OK,MessageBoxIcon.Information);
                RenderAll(); return;
            }

            RenderAll();
            MaybeAISequence();
        }

        void MaybeAISequence()
        {
            if(gs.WaitingNext || gs.WaitingCollect) return;
            aiQueue.Clear();
            int p = gs.ToPlay;
            while(p!=0)
            {
                aiQueue.Enqueue(p);
                if(gs.Current.Cards.Count + aiQueue.Count >= 4) break;
                p = (p+1)%4;
            }
            if(aiQueue.Count>0) aiTimer.Start();
        }

        // ======= Drawing helpers =======
        void DrawBack(Graphics g, Rectangle rect){ DrawCard(g, new Card{Suit=Suit.Spades, Rank="A"}, rect, true); }
        void DrawCard(Graphics g, Card c, Rectangle rect, bool back)
        {
            int w=rect.Width, h=rect.Height;
            Rectangle rr=new Rectangle(rect.X, rect.Y, w-1, h-1);
            using(var path=new System.Drawing.Drawing2D.GraphicsPath())
            {
                int rad=10;
                path.AddArc(rr.Left, rr.Top, rad, rad, 180, 90);
                path.AddArc(rr.Right-rad, rr.Top, rad, rad, 270, 90);
                path.AddArc(rr.Right-rad, rr.Bottom-rad, rad, rad, 0, 90);
                path.AddArc(rr.Left, rr.Bottom-rad, rad, rad, 90, 90);
                path.CloseFigure();
                g.FillPath(back? new SolidBrush(Color.FromArgb(30,60,110)) : Brushes.White, path);
                g.DrawPath(Pens.Gray, path);
            }
            if(back) return;
            bool red = SuitIsRed[(int)c.Suit];
            using(var f1=new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold))
            using(var f2=new Font(FontFamily.GenericSansSerif, 28, FontStyle.Regular))
            {
                Brush fore = red? Brushes.Maroon: Brushes.Black;
                g.DrawString(c.Rank, f1, fore, rect.X+8, rect.Y+6);
                g.DrawString(SuitGlyph[(int)c.Suit], f1, fore, rect.X+8, rect.Y+22);
                var sf=new StringFormat(); sf.Alignment=StringAlignment.Center; sf.LineAlignment=StringAlignment.Center;
                g.DrawString(SuitGlyph[(int)c.Suit], f2, fore, new RectangleF(rect.X, rect.Y, rect.Width-1, rect.Height-1), sf);
            }
        }
    }
}
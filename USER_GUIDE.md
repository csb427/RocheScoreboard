# Roche Scoreboard - User Guide

## 🎮 Control Panel Overview

The Control Panel (Main Window) is your command center for managing the scoreboard display.

---

## 📊 Top Control Bar

### Available Controls

| Button | Function | Shortcut |
|--------|----------|----------|
| 📺 Show Scoreboard | Opens/brings scoreboard to front | - |
| ▶ Start | Begins match clock | - |
| ⏸ Pause | Pauses match clock | - |
| ⏭ End Quarter | Advances to next quarter, shows break screen | - |
| ↶ Undo | Removes last score event | - |
| 🔄 New Game | Resets match for new game | - |
| ⚡ Toggle Layout | Switches scoreboard display mode | - |
| 🌙 Dark Mode | Toggle between dark/light theme | - |

---

## 📑 Tabs

### 1️⃣ **Teams & Colors** Tab

#### Team Setup
- **Home Team**: Enter team name and set colors
- **Away Team**: Enter team name and set colors
- Both teams require a name to display on scoreboard

#### Color Customization
For each team, you can set:

1. **Primary Color** (🎨 Pick Home/Away Primary Color)
   - Used for scoreboard background gradient
   - Affects goal animation colors
   - Must be valid hex color (#RRGGBB)

2. **Text Color** (🎨 Pick Home/Away Text Color)
   - Color of team name and scores
   - Ensure good contrast for readability
   - Must be valid hex color (#RRGGBB)

#### How to Change Colors
1. Click the color picker button
2. Enter hex color code (e.g., #D60000)
   - Include the # symbol
   - Use valid 6-digit hex code
   - Examples: #FF0000 (red), #0000FF (blue)
3. Press OK to apply
4. Scoreboard updates in real-time

#### Apply Team Names
- Click "✓ Apply Team Names" to update scoreboard display
- Names must be entered before applying

---

### 2️⃣ **Scoring** Tab

#### Quick Scoring

| Button | Effect | Points |
|--------|--------|--------|
| ➕ +6 Goal | Add goal for team | +6 |
| ➕ +1 Behind | Add behind for team | +1 |

#### How Scoring Works
```
Goals.Behinds.Total
Example: 7.3.45
         ↓ ↓ ↓
      7 Goals + 3 Behinds = 45 Total Points
      (7×6 + 3×1 = 45)
```

#### Scoreboard Display
When you add a score:
1. ✅ Score updates on scoreboard instantly
2. ✅ Goal animation plays (if goal scored)
3. ✅ Goal animation uses team color
4. ✅ Event is logged in Event Log tab
5. ✅ Match state auto-saves

---

### 3️⃣ **Messages** Tab (NEW!)

#### Sending Custom Messages

1. **Type Message**
   - Click in the text box
   - Type your message (up to multiple lines)
   - Supports any text

2. **Send Message**
   - Click "📤 Send Message" button
   - Message displays on scoreboard for 4.2 seconds
   - Text box automatically clears
   - Ready for next message

#### Message Examples

- **Halftime Announcements**
  - "Ladies and gentlemen, welcome back from halftime!"
  - "The second half is underway!"

- **Player Updates**
  - "Player #7 has taken the field!"
  - "Substitution: Player #12 off, Player #15 on"

- **Score Updates**
  - "Home team leads 24-18 at the end of Q2!"

- **Sponsor Messages**
  - "This scoreboard brought to you by [Sponsor Name]"

- **Stadium Announcements**
  - "Next match starts in 30 minutes"
  - "Concessions are now open"

#### Auto-Scrolling Messages (Bottom Bar)

The scoreboard bottom displays rotating messages:
- Default messages rotate every 5 seconds
- Can be customized in control panel
- Shows continuously

---

### 4️⃣ **Event Log** Tab

#### Event History

- **Displays**: All score events in order
- **Format**: "Home Goal" or "Away Behind" + timestamp
- **Latest**: Most recent event at top
- **Management**: Events clear on "New Game"

#### Event Details

Each entry shows:
- Team (Home/Away)
- Score type (Goal/Behind)
- Current match state
- Quarter and time

---

## 🎯 Scoreboard Display Modes

### Side-by-Side Mode (Default)
```
┌─────────────────────────────┐
│   Q1    00:00   SIDE-BY-SIDE│
├─────────────────────────────┤
│  HOME       │      AWAY     │
│  GOALS: 7   │   GOALS: 5    │
│  BEHINDS: 2 │   BEHINDS: 1  │
│  TOTAL 44   │   TOTAL 31    │
│             │               │
└─────────────────────────────┘
```

**Best For**: 
- Widescreen displays
- Traditional broadcast setups
- Direct comparison viewing

---

### Stacked Mode
```
┌─────────────────────────────┐
│   Q1    00:00     STACKED   │
├─────────────────────────────┤
│  HOME                       │
│  GOALS: 7                   │
│  BEHINDS: 2                 │
│  TOTAL 44                   │
│                             │
│  AWAY                       │
│  GOALS: 5                   │
│  BEHINDS: 1                 │
│  TOTAL 31                   │
│                             │
└─────────────────────────────┘
```

**Best For**:
- Vertical displays
- Mobile viewing
- Tight spaces
- Enhanced vertical information

---

## 🔄 Workflow Example: Complete Match

### Before Match
1. Go to **Teams & Colors** tab
2. Enter team names
3. Set primary colors for each team
4. Set text colors (usually white)
5. Click "✓ Apply Team Names"

### Start of Match
1. Go to **Scoring** tab
2. Click "📺 Show Scoreboard" to open display
3. Click "▶ Start" to begin clock
4. Scoreboard shows Q1 with timer

### During Match
1. Click "➕ +6 Goal" or "➕ +1 Behind" to add scores
2. Scores update instantly
3. Goal animations play automatically
4. Use **Messages** tab for announcements
5. Watch Event Log for history

### Between Quarters
1. Click "⏭ End Quarter" 
2. Break screen shows (e.g., "QUARTER TIME")
3. Displays current scores
4. Make announcements via **Messages** tab
5. Click "▶ Start" to resume

### End of Match
1. Click "⏭ End Quarter" on Q4
2. Shows "FULL TIME" screen
3. Display final scores
4. Click "🔄 New Game" to reset for next match

---

## ⚙️ Settings & Customization

### Theme Control
- **Dark Mode** (default): Professional broadcast look
- **Light Mode**: Alternative bright display
- Toggle at top of control panel

### Layout Switching
- Click "⚡ Toggle Layout" to switch modes
- Changes apply immediately
- Scoreboard stays active

### Color Validation

**Valid Hex Colors:**
```
#FF0000  - Red
#00FF00  - Green
#0000FF  - Blue
#FFFF00  - Yellow
#FF00FF  - Magenta
#00FFFF  - Cyan
#FFFFFF  - White
#000000  - Black
```

**Color Picker Tips:**
- Always include the # symbol
- Use 6-digit hex codes only
- Invalid colors fall back to defaults
- Examples are shown in picker dialog

---

## 🎨 Display Customization

### Scoreboard Colors
- **Team Names**: Determined by team text color
- **Scores**: Inherit team text color
- **Total Score Box**: Always gold highlight
- **Background**: Gradient of team primary colors

### Message Bar
- Shows rotating announcements
- Updates every 5 seconds
- Customizable messages per match
- Professional scrolling ticker effect

---

## ⏱️ Time Management

### Clock Display
- **Format**: MM:SS (minutes:seconds)
- **Updates**: Every 200ms for smoothness
- **Auto-increments**: While running

### Quarter Management
- **Q1, Q2, Q3, Q4**: Standard match quarters
- **Auto-advance**: Click "⏭ End Quarter"
- **Display**: Shows current quarter on scoreboard

---

## 🎬 Animations & Effects

### Goal Animation
- **Trigger**: Click "➕ +6 Goal"
- **Effect**: Team-colored striped background
- **Text**: Large "GOAL" text
- **Duration**: ~2.6 seconds
- **Sound**: (Optional, can be added)

### Custom Message Animation
- **Trigger**: Send custom message
- **Effect**: Scale + fade animation
- **Duration**: ~4.2 seconds
- **Visibility**: Full-screen overlay

### Break Screen
- **Trigger**: End quarter
- **Content**: Title + current scores
- **Duration**: Until match resumes
- **Update**: Shows live score during break

---

## 💾 Auto-Save Feature

### What Gets Saved
- Current match state
- All scores
- Team names
- Quarter progression
- Event history

### When It Saves
- Automatically after every score change
- Every time match state updates
- When app is running

### Restore Previous Match
- Automatically loads last saved match on startup
- Useful if app crashes
- Can start fresh with "🔄 New Game"

---

## 🆘 Troubleshooting

### Issue: Scoreboard won't open
- **Solution**: Click "📺 Show Scoreboard"
- **Alternative**: Click "▶ Start" (auto-opens)

### Issue: Scores not updating
- **Solution**: Ensure scoreboard window is visible
- Check that "✓ Apply Team Names" was clicked
- Try toggling layout with "⚡ Toggle Layout"

### Issue: Colors not showing
- **Solution**: Verify hex code format (#RRGGBB)
- Example: #D60000 (not D60000 or rd60000)
- Use only valid hex digits (0-9, A-F)

### Issue: Messages not appearing
- **Solution**: Ensure scoreboard is open and on live screen
- Messages don't show on break screens
- Type complete message before sending

### Issue: Clock not starting
- **Solution**: Click "▶ Start" button
- Verify scoreboard is open
- Check that "⏸ Pause" wasn't clicked

---

## 🎯 Best Practices

### Setup
1. ✅ Always set team names first
2. ✅ Choose colors with good contrast
3. ✅ Test display mode before match
4. ✅ Verify clock is working

### During Match
1. ✅ Keep event log visible for reference
2. ✅ Use messages for announcements
3. ✅ Pause clock for stoppages
4. ✅ Use undo if score is wrong

### Color Selection
1. ✅ Use team brand colors
2. ✅ Ensure readability on dark background
3. ✅ Test contrast before match
4. ✅ Keep text color consistent (white/black)

---

## 📱 Display Tips

### For Broadcast
- Use dark mode (professional look)
- Set full-screen scoreboard window
- Position on secondary monitor
- Use Side-by-Side layout
- Test colors beforehand

### For Stadium/Live Event
- Use bright, contrasting colors
- Consider viewing distance
- Test legibility from far away
- Use larger display window
- Update messages frequently

### For Recording/Streaming
- Use high-contrast colors
- Test on actual camera/stream
- Adjust brightness/saturation
- Record test footage first
- Have backup message queue

---

## 🚀 Advanced Features

### Custom Message Queue
Set up multiple messages to scroll:
```
1. "Welcome to Roche Scoreboard"
2. "Home Team vs Away Team"
3. "Q1 in progress - Time: 15:32"
4. [Sponsor message]
5. "Follow on social media @RocheScoreboard"
```

### Layout Switching Strategy
- **During Play**: Use Side-by-Side for clarity
- **Replays**: Can switch to Stacked for variety
- **Breaks**: Experiment with layout preference
- **Live Tests**: Try both modes before broadcast

### Theme Management
- **Dark Mode**: Standard broadcast format
- **Light Mode**: Indoor gym/bright lighting
- **Toggle**: Between halves if lighting changes

---

## 📞 Support

For issues or questions:
1. Check scoreboard is running
2. Verify all required fields are filled
3. Ensure team names are applied
4. Test with default values first
5. Review this guide for common scenarios

---

**Ready to broadcast like a pro! 🏆**

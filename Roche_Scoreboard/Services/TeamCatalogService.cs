using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Verified AFL, VAFA, APS and AGSV team identities.</summary>
public static class TeamCatalogService
{
    private const string VafaRoot = "https://cdn-vafanewsite.b-cdn.net/wp-content/uploads/";

    private static readonly IReadOnlyList<TeamCatalogEntry> Teams = BuildTeams();

    public static IReadOnlyList<TeamCatalogEntry> GetTeams() => Teams;

    private static IReadOnlyList<TeamCatalogEntry> BuildTeams()
    {
        List<TeamCatalogEntry> teams =
        [
            new("afl-adelaide", "Adelaide Crows", "ADE", "AFL", "SA", "#002B5C", "#E21937", "Adelaide Football Club"),
            new("afl-brisbane", "Brisbane Lions", "BRI", "AFL", "QLD", "#7C003E", "#FDBE57", "Brisbane Lions"),
            new("afl-carlton", "Carlton", "CAR", "AFL", "VIC", "#031A29", "#FFFFFF", "Carlton Football Club"),
            new("afl-collingwood", "Collingwood", "COL", "AFL", "VIC", "#000000", "#FFFFFF", "Collingwood Football Club"),
            new("afl-essendon", "Essendon", "ESS", "AFL", "VIC", "#CC2031", "#000000", "Essendon Football Club"),
            new("afl-fremantle", "Fremantle", "FRE", "AFL", "WA", "#2A0D54", "#FFFFFF", "Fremantle Football Club"),
            new("afl-geelong", "Geelong Cats", "GEE", "AFL", "VIC", "#001F46", "#FFFFFF", "Geelong Football Club"),
            new("afl-gold-coast", "Gold Coast SUNS", "GCS", "AFL", "QLD", "#E4032E", "#FFDC00", "Gold Coast Suns"),
            new("afl-gws", "GWS GIANTS", "GWS", "AFL", "NSW", "#F15A22", "#2B2B2B", "Greater Western Sydney Giants"),
            new("afl-hawthorn", "Hawthorn", "HAW", "AFL", "VIC", "#4D2004", "#FBBF15", "Hawthorn Football Club"),
            new("afl-melbourne", "Melbourne", "MEL", "AFL", "VIC", "#0F1131", "#CC2031", "Melbourne Football Club"),
            new("afl-north-melbourne", "North Melbourne", "NTH", "AFL", "VIC", "#003E7E", "#FFFFFF", "North Melbourne Football Club"),
            new("afl-port-adelaide", "Port Adelaide", "PORT", "AFL", "SA", "#008AAB", "#000000", "Port Adelaide Football Club"),
            new("afl-richmond", "Richmond", "RIC", "AFL", "VIC", "#FED102", "#000000", "Richmond Football Club"),
            new("afl-st-kilda", "St Kilda", "STK", "AFL", "VIC", "#ED1B2F", "#000000", "St Kilda Football Club"),
            new("afl-sydney", "Sydney Swans", "SYD", "AFL", "NSW", "#E1251B", "#FFFFFF", "Sydney Swans"),
            new("afl-west-coast", "West Coast Eagles", "WCE", "AFL", "WA", "#003087", "#F2A900", "West Coast Eagles"),
            new("afl-western-bulldogs", "Western Bulldogs", "WB", "AFL", "VIC", "#014896", "#ED1B2F", "Western Bulldogs")
        ];

        const string vafa = """
ajax|AJAX|AJX|#002B5C|#FFFFFF|2024/06/AJAX-FC-logo.png
albert-park|Albert Park|AP|#C8102E|#FFFFFF|2024/05/Albert-Park.webp
aquinas|Aquinas|AQU|#7A0019|#FFCC33|2024/06/Aquinas-Logo-transparent.png
beaumaris|Beaumaris|BEA|#0057B8|#FFCC00|2024/06/beaumaris-logo.png
box-hill-north|Box Hill North|BHN|#003DA5|#FFFFFF|2024/05/Box-Hill-North-Logo.jpg
brunswick|Brunswick|BRU|#111111|#FFFFFF|2019/02/brunswickfc-logo.png
canterbury|Canterbury|CAN|#00205B|#FFFFFF|2025/01/caterbury-fc-25.png
caulfield-grammarians|Caulfield Grammarians|CG|#003B71|#FFB81C|2024/06/caulfield-grammarians-logo.png
chadstone|Chadstone|CHA|#111111|#F2A900|2024/06/Chadstone-afc-logo.png
collegians|Collegians|COLL|#582C83|#FFB81C|2024/05/collegians-transparent-logo.png
de-la-salle|De La Salle|DLS|#003DA5|#FFCC00|2024/06/de-la-salle-logo.png
eley-park|Eley Park|EP|#00843D|#FFFFFF|2024/06/Eley-Park-Sharks-transparent-logo.png
elsternwick|Elsternwick|ELS|#C8102E|#FFFFFF|2024/06/elsternwick-afc-logo.png
fitzroy|Fitzroy|FIT|#7A0019|#003B71|2024/05/FitzroyFC-logo-1.png
glen-eira|Glen Eira/Old McKinnon|GEOM|#003B71|#FFB81C|2024/06/glen-eira-logo.png
hampton-rovers|Hampton Rovers|HR|#006341|#FFCC00|2024/05/Hampton-Rovers-Logo-2018-FINAL.png
hawthorn|Hawthorn|HAW|#4D2004|#FBBF15|2024/06/hawthorn-afc-logo.png
kew|Kew|KEW|#6A1B2B|#FFFFFF|2024/06/Kew-logo.png
la-trobe|La Trobe University|LTU|#C8102E|#FFFFFF|2024/06/La-Trobe-FC-white-logo.png
mhsob|MHSOB|MHS|#003B71|#FFFFFF|2024/06/unicorns-logo-transparent.png
marcellin|Marcellin|MAR|#003DA5|#C8102E|2023/11/marcellin-logo-be0b619e.png
masala|Masala Dandenong|MAS|#E87722|#111111|2019/02/MASALA-final.png
mazenod|Mazenod|MAZ|#003B71|#FFB81C|2024/06/mazenod-white-transparent.png
mcc|MCC FC|MCC|#C8102E|#FFFFFF|2024/06/MCCFC-Flag.png
muwfc|MUWFC|MUW|#003B71|#FFFFFF|2024/06/MUWFC-logo.png
monash-blues|Monash Blues|MON|#003B71|#FFFFFF|2024/06/monash-blues-logo-reverse.png
north-brunswick|North Brunswick|NB|#111111|#FFFFFF|2024/06/North-Brunswick-FC-logo.png
oakleigh|Oakleigh|OAK|#6A1B2B|#FFB81C|2024/06/Oakleigh-AFC.png
ormond|Ormond|ORM|#7A0019|#003B71|2024/06/Ormond-AFC-logo.png
old-brighton|Old Brighton|OB|#C8102E|#003B71|2024/05/old-brighton-logo.png
old-camberwell|Old Camberwell|OC|#003B71|#FFB81C|2024/06/old-camberwell-logo.png
old-carey|Old Carey|OCG|#4B2E83|#FFB81C|2024/06/Old-Carey.png
old-geelong|Old Geelong|OG|#003B71|#FFFFFF|2024/06/Old-Geelong-logo.png
old-haileybury|Old Haileybury|OH|#7A0019|#FFB81C|2024/06/Old-Haileybury-logo.png
old-ivanhoe|Old Ivanhoe|OI|#582C83|#FFFFFF|2024/06/ivanhoe-old-logo.png
old-melburnians|Old Melburnians|OM|#003B71|#C8102E|2024/05/Old-Melbourians-logo.png
old-peninsula|Old Peninsula|OP|#003B71|#FFB81C|2024/06/Old-Peninsula-logo.png
old-scotch|Old Scotch|OS|#5C2D91|#FFB81C|2024/05/Old-Scotch-logo.png
old-trinity|Old Trinity|OT|#006341|#FFB81C|2024/06/Old-Trinity-logo.png
old-xaverians|Old Xaverians|OX|#C8102E|#111111|2024/05/Old-Xavarians-logo.png
old-yarra|Old Yarra Cobras|OYC|#111111|#FFB81C|2022/11/Old-Yarra-Cobras-Logo-300x297-1.png
parkdale|Parkdale Vultures|PV|#003B71|#FFB81C|2024/06/Parkdale-Vultures-logo.png
parkside|Parkside|PS|#C8102E|#FFFFFF|2015/02/Parkside-FC_LOGO_4C.png
pegs|PEGS|PEGS|#003B71|#FFB81C|2024/06/PEGSFC-transparent.png
port-melbourne|Port Melbourne Chargers|PMC|#111111|#00A6B2|2025/03/Port-Melbourne-Chargers-logo-rev.png
power-house|Power House|PH|#C8102E|#FFFFFF|2024/06/power-house-logo.png
prahran|Prahran|PRA|#003B71|#6A1B2B|2022/12/prahran-fc-logo.png
preston|Preston Bullants|PRE|#C8102E|#FFFFFF|2024/06/preston-flag.png
richmond-central|Richmond Central|RC|#111111|#FFCC00|2024/06/Richmond-Central-logo.png
south-melbourne|South Melbourne Districts|SMD|#C8102E|#FFFFFF|2024/06/south-melbourne-districts-logo.png
st-bedes|St Bedes/Mentone Tigers|SBMT|#003B71|#FFB81C|2024/06/SBMT-logo.png
st-bernards|St Bernards|STB|#006341|#FFB81C|2024/05/St-Bernards-logo.png
skob|St Kevin’s Old Boys|SKOB|#006341|#FFB81C|2024/05/SKOB-logo.png
st-marys|St Mary’s Salesian|SMS|#003B71|#FFB81C|2024/06/St-Marys-Salesian-logo.png
swinburne|Swinburne University|SWIN|#C8102E|#FFFFFF|2024/06/Swimburne-Razorbacks-logo.png
therry|Therry Penola|TP|#003B71|#C8102E|2022/03/therry-penola-afc-logo.jpg
uhs-vu|UHS-VU|UHS|#006341|#FFFFFF|2024/06/UHSVU-vulture-transparent.webp
uni-blacks|Uni Blacks|UB|#111111|#FFFFFF|2024/05/Uni-Blacks-logo.png
uni-blues|Uni Blues|UBL|#003B71|#FFFFFF|2024/05/uni-blue-logo.png
wattle-park|Wattle Park|WP|#006341|#FFB81C|2024/06/wattle-park-animals-logo.png
westbourne|Westbourne Grammarians|WG|#003B71|#FFB81C|2024/06/westbourne.png
williamstown|Williamstown CYMS|WCYMS|#003B71|#FFB81C|2024/06/Williamstown-CYMS.png
west-brunswick|West Brunswick|WB|#111111|#C8102E|2024/06/west-brunswick-logo.png
whitefriars|Whitefriars|WHT|#003B71|#FFB81C|2024/06/whitefriars-logo.png
""";
        foreach (string row in vafa.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] p = row.Split('|');
            teams.Add(new($"vafa-{p[0]}", p[1], p[2], "VAFA", "VIC", p[3], p[4], VafaRoot + p[5]));
        }

        AddSchool(teams, "APS", "brighton-grammar", "Brighton Grammar School", "BGS", "#003B71", "#C8102E", "https://upload.wikimedia.org/wikipedia/commons/9/92/Brighton_Grammar_School_Logo.jpg");
        AddSchool(teams, "APS", "carey", "Carey Baptist Grammar School", "CY", "#4B2E83", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/f/f6/Carey_Baptist_Grammar_School_logo_2017.png");
        AddSchool(teams, "APS", "caulfield", "Caulfield Grammar School", "CGS", "#003B71", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/b/b7/Caulfield_Grammar_School_Crest%2C_Private_School_located_in_the_Melbournse_suburb_of_Caulfield%2C_Victoria%2C_Australia.png");
        AddSchool(teams, "APS", "geelong-college", "The Geelong College", "GC", "#006341", "#FFFFFF", "https://upload.wikimedia.org/wikipedia/en/1/17/Geelong_college_crest.png");
        AddSchool(teams, "APS", "geelong-grammar", "Geelong Grammar School", "GGS", "#003B71", "#FFFFFF", "https://upload.wikimedia.org/wikipedia/commons/9/93/Geelong_Grammar_School_%28crest%29.png");
        AddSchool(teams, "APS", "haileybury", "Haileybury", "HY", "#7A0019", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/7/72/Haileybury_au_crest.svg");
        AddSchool(teams, "APS", "melbourne-grammar", "Melbourne Grammar School", "MGS", "#003B71", "#C8102E", "https://upload.wikimedia.org/wikipedia/en/d/d9/Melbournegrammarcrest.jpg");
        AddSchool(teams, "APS", "st-kevins", "St Kevin’s College", "SKC", "#006341", "#FFB81C", "https://www.stkevins.vic.edu.au/images/logo.svg");
        AddSchool(teams, "APS", "scotch", "Scotch College", "SC", "#5C2D91", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/3/38/ScotchCollegeMelbourneCrest.jpg");
        AddSchool(teams, "APS", "wesley", "Wesley College", "WC", "#582C83", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/6/6a/Wesley_College%2C_Melbourne_-_Crest.png");
        AddSchool(teams, "APS", "xavier", "Xavier College", "XC", "#C8102E", "#111111", "https://upload.wikimedia.org/wikipedia/en/6/63/Xavier_College_Crest.png");

        AddSchool(teams, "AGSV", "assumption", "Assumption College", "ACK", "#003B71", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/f/f9/AssumptionKilmoreLogo.jpg");
        AddSchool(teams, "AGSV", "camberwell", "Camberwell Grammar School", "CGS", "#003B71", "#FFB81C", "https://www.cgs.vic.edu.au/images/footer-logo.svg");
        AddSchool(teams, "AGSV", "ivanhoe", "Ivanhoe Grammar School", "IGS", "#582C83", "#FFFFFF", "https://www.ivanhoe.com.au/wp-content/uploads/2019/06/IvanhoeGS_Line_Logo_PMS411.png");
        AddSchool(teams, "AGSV", "marcellin", "Marcellin College", "MCC", "#003DA5", "#C8102E", "https://cdn.prod.website-files.com/63297a19b276572e2343c0da/679b07255435c6b6e8ab338d_2024_MarcellinCollege_Logo_Stacked_RGB_D1_Maroon.webp");
        AddSchool(teams, "AGSV", "mentone", "Mentone Grammar", "MG", "#003B71", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/9/9d/Mentone_Grammar_School_logo.svg");
        AddSchool(teams, "AGSV", "peninsula", "Peninsula Grammar", "PG", "#003B71", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/3/36/Peninsula_Grammar_%28crest%29.jpg");
        AddSchool(teams, "AGSV", "pegs", "Penleigh and Essendon Grammar School", "PEGS", "#003B71", "#FFB81C", "https://upload.wikimedia.org/wikipedia/commons/9/91/PegsCrest.jpg");
        AddSchool(teams, "AGSV", "trinity", "Trinity Grammar School", "TGS", "#006341", "#FFB81C", "https://upload.wikimedia.org/wikipedia/en/c/c5/Trinity_Grammar_School%2C_Kew_logo.png");
        AddSchool(teams, "AGSV", "yarra-valley", "Yarra Valley Grammar", "YVG", "#003B71", "#FFB81C", "https://yvg-vic.s3.amazonaws.com/uploads/YVG_Logo_Standard_Logo.svg");
        return teams;
    }

    private static void AddSchool(List<TeamCatalogEntry> teams, string league, string id, string name, string abbreviation, string primary, string secondary, string page)
        => teams.Add(new($"{league.ToLowerInvariant()}-{id}", name, abbreviation, league, "VIC", primary, secondary, page));
}

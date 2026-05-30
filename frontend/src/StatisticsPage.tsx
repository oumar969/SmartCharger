import { useQuery } from "@tanstack/react-query";
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid,
  ResponsiveContainer, Cell
} from "recharts";
import axios from "axios";

const SAPI = "http://localhost:5000/api/sessions";

interface MonthlyStats {
  month: string;
  savingsDKK: number;
  co2Saved: number;
  sessions: number;
}

interface Co2Report {
  month: string;
  totalHours: number;
  avgCo2: number;
  greenPct: number;
  co2SavedGrams: number;
  treesEquivalent: number;
  shareText: string;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const fetchJson = (url: string): Promise<any> => axios.get(url).then(r => r.data);

function monthLabel(m: string) {
  const [y, mo] = m.split("-");
  const names = ["Jan","Feb","Mar","Apr","Maj","Jun","Jul","Aug","Sep","Okt","Nov","Dec"];
  return `${names[parseInt(mo) - 1]} ${y}`;
}

export default function StatisticsPage() {
  const { data: monthly = [] } = useQuery<MonthlyStats[]>({
    queryKey: ["monthly"],
    queryFn:  () => fetchJson(`${SAPI}/monthly`),
  });

  const { data: report } = useQuery<Co2Report>({
    queryKey: ["co2report"],
    queryFn:  () => fetchJson(`${SAPI}/co2report`),
    retry: false,
  });

  const totalSavings  = monthly.reduce((s, m) => s + m.savingsDKK, 0);
  const totalCo2      = monthly.reduce((s, m) => s + m.co2Saved, 0);
  const totalSessions = monthly.reduce((s, m) => s + m.sessions, 0);

  return (
    <div className="stats-page">

      {/* ── Totals ── */}
      <span className="eyebrow">Din samlede besparelse</span>
      <div className="stats-grid" style={{ marginBottom: 20 }}>
        <div className="stat-card">
          <span>{Math.round(totalSavings * 100)} øre</span>
          <small>Sparet i alt</small>
        </div>
        <div className="stat-card green">
          <span>{Math.round(totalCo2 / 1000)} kg</span>
          <small>CO₂ undgået</small>
        </div>
        <div className="stat-card">
          <span>{totalSessions}</span>
          <small>Opladninger</small>
        </div>
        <div className="stat-card blue">
          <span>{totalSessions > 0 ? Math.round(totalSavings * 100 / totalSessions) : 0} øre</span>
          <small>Gns. pr. opladning</small>
        </div>
      </div>

      {/* ── Monthly savings chart ── */}
      {monthly.length > 0 && (
        <>
          <span className="eyebrow">Besparelse pr. måned</span>
          <div className="chart-wrapper" style={{ marginBottom: 20 }}>
            <ResponsiveContainer width="100%" height={200}>
              <BarChart data={monthly} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="month" tickFormatter={monthLabel} tick={{ fontSize: 11 }} />
                <YAxis tickFormatter={v => `${Math.round(v * 100)}ø`} tick={{ fontSize: 11 }} width={42} />
                <Tooltip
                  formatter={(v) => [`${Math.round(Number(v) * 100)} øre`, "Besparelse"]}
                  labelFormatter={(l) => monthLabel(String(l))}
                />
                <Bar dataKey="savingsDKK" radius={[4, 4, 0, 0]}>
                  {monthly.map((_, i) => (
                    <Cell key={i} fill="#3ecf8e" />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>

          <span className="eyebrow">CO₂ undgået pr. måned</span>
          <div className="chart-wrapper" style={{ marginBottom: 20 }}>
            <ResponsiveContainer width="100%" height={200}>
              <BarChart data={monthly} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="month" tickFormatter={monthLabel} tick={{ fontSize: 11 }} />
                <YAxis tickFormatter={v => `${v}g`} tick={{ fontSize: 11 }} width={42} />
                <Tooltip
                  formatter={(v) => [`${Math.round(Number(v))} g CO₂`, "Undgået"]}
                  labelFormatter={(l) => monthLabel(String(l))}
                />
                <Bar dataKey="co2Saved" radius={[4, 4, 0, 0]}>
                  {monthly.map((_, i) => (
                    <Cell key={i} fill="#24b47e" />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </>
      )}

      {/* ── CO₂ monthly report ── */}
      {report && (
        <>
          <span className="eyebrow">Grøn rapport — {monthLabel(report.month)}</span>
          <div className="co2-report">

            <div className="green-circle">
              <span className="green-pct">{report.greenPct}%</span>
              <small>grøn energi</small>
            </div>

            <div className="co2-facts">
              <div className="fact">
                <span>🌳 {report.treesEquivalent} træer</span>
                <small>CO₂-aftryk svarende til</small>
              </div>
              <div className="fact">
                <span>{report.co2SavedGrams.toLocaleString("da-DK")} g</span>
                <small>CO₂ undgået denne måned</small>
              </div>
              <div className="fact">
                <span>{report.avgCo2.toFixed(0)} g/kWh</span>
                <small>Gns. CO₂ ved opladning</small>
              </div>
              <div className="fact">
                <span>{report.totalHours} timer</span>
                <small>Ladet i alt</small>
              </div>
            </div>

            <button
              className="share-btn"
              onClick={() => {
                const url = `https://www.linkedin.com/shareArticle?mini=true&text=${encodeURIComponent(report.shareText)}`;
                window.open(url, "_blank");
              }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                <path d="M20.447 20.452h-3.554v-5.569c0-1.328-.027-3.037-1.852-3.037-1.853 0-2.136 1.445-2.136 2.939v5.667H9.351V9h3.414v1.561h.046c.477-.9 1.637-1.85 3.37-1.85 3.601 0 4.267 2.37 4.267 5.455v6.286zM5.337 7.433a2.062 2.062 0 01-2.063-2.065 2.064 2.064 0 112.063 2.065zm1.782 13.019H3.555V9h3.564v11.452zM22.225 0H1.771C.792 0 0 .774 0 1.729v20.542C0 23.227.792 24 1.771 24h20.451C23.2 24 24 23.227 24 22.271V1.729C24 .774 23.2 0 22.222 0h.003z"/>
              </svg>
              Del på LinkedIn
            </button>
          </div>
        </>
      )}

      {monthly.length === 0 && !report && (
        <div className="empty-state">
          <p>Ingen data endnu.</p>
          <small>Gem din første opladning på forsiden for at se statistik her.</small>
        </div>
      )}
    </div>
  );
}

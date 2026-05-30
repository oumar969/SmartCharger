import { useState, useMemo } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ComposedChart, Bar, Line, XAxis, YAxis, Tooltip, CartesianGrid,
  Cell, ResponsiveContainer, ReferenceLine, Legend
} from "recharts";
import axios from "axios";
import "./App.css";

interface HourData {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  priceArea: string;
}

interface ChargeRecommendation {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  isRecommended: boolean;
}

interface ChargeWindow {
  windowStart: string;
  windowEnd: string;
  totalCostDKK: number;
  averagePriceDKK: number;
  averageCo2: number;
  hours: ChargeRecommendation[];
}

interface PriceForecast {
  hourStart: string;
  forecastedPriceDKK: number;
  lowerBound: number;
  upperBound: number;
}

interface SessionStats {
  totalSessions: number;
  totalSavingsDKK: number;
  totalCo2Saved: number;
  avgSavingPerSession: number;
}

type Strategy = "Cheapest" | "Greenest";

const API   = "http://localhost:5000/api/elspot";
const SAPI  = "http://localhost:5000/api/sessions";
const AREAS = ["DK1", "DK2"];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const fetchJson = (url: string): Promise<any> => axios.get(url).then(r => r.data);

// Format ISO timestamp → "HH:mm"
const fmt = (iso: string) =>
  new Date(iso).toLocaleTimeString("da-DK", { hour: "2-digit", minute: "2-digit" });

// kr/kWh → øre/kWh
const toOere = (kr: number) => Math.round(kr * 100);

export default function App() {
  const [hours,    setHours]    = useState(4);
  const [area,     setArea]     = useState<string>("DK2");
  const [deadline, setDeadline] = useState("07:00");
  const [strategy, setStrategy] = useState<Strategy>("Cheapest");

  // Build deadline ISO — if deadline time has already passed today, use tomorrow
  const { deadlineISO, deadlineError } = useMemo(() => {
    const now = new Date();
    const parts = deadline.split(":");
    const h = parseInt(parts[0] ?? "", 10);
    const m = parseInt(parts[1] ?? "", 10);
    if (isNaN(h) || isNaN(m)) {
      const tomorrow = new Date(now);
      tomorrow.setDate(tomorrow.getDate() + 1);
      return { deadlineISO: tomorrow.toISOString(), deadlineError: null };
    }
    const d = new Date(now);
    d.setHours(h, m, 0, 0);
    const hoursUntil = (d.getTime() - now.getTime()) / 3600000;
    if (hoursUntil < hours) d.setDate(d.getDate() + 1);
    const finalHours = (d.getTime() - now.getTime()) / 3600000;
    const error = finalHours < hours
      ? `Kun ${finalHours.toFixed(1)} t til deadline — ikke nok tid til ${hours} timers opladning`
      : null;
    return { deadlineISO: d.toISOString(), deadlineError: error };
  }, [deadline, hours]);

  const { data: recommendations = [], isLoading, isError } = useQuery<ChargeRecommendation[]>({
    queryKey: ["recommendations", hours, area, strategy],
    queryFn:  () => fetchJson(`${API}/recommendations?hours=${hours}&area=${area}&strategy=${strategy}`),
  });

  const { data: merged = [] } = useQuery<HourData[]>({
    queryKey: ["merged", area],
    queryFn:  () => fetchJson(`${API}/merged?area=${area}`),
  });

  const { data: window } = useQuery<ChargeWindow | null>({
    queryKey: ["window", hours, area, deadlineISO, strategy],
    retry: false,
    enabled: !deadlineError,
    queryFn: () =>
      axios.get<ChargeWindow>(`${API}/window?hours=${hours}&area=${area}&deadline=${encodeURIComponent(deadlineISO)}&strategy=${strategy}`)
        .then(r => r.data)
        .catch(e => e?.response?.status === 404 ? null : Promise.reject(e)),
  });

  const qc = useQueryClient();

  const { data: forecast = [] } = useQuery<PriceForecast[]>({
    queryKey: ["forecast", area],
    queryFn:  () => fetchJson(`${API}/forecast?area=${area}&horizon=24`),
    staleTime: 60 * 60 * 1000,
  });

  const { data: stats } = useQuery<SessionStats>({
    queryKey: ["stats"],
    queryFn:  () => fetchJson(`${SAPI}/stats`),
  });

  const saveSession = useMutation({
    mutationFn: () => axios.post(SAPI, {
      windowStart:  window?.windowStart,
      windowEnd:    window?.windowEnd,
      hours,
      avgPriceDKK:  window?.averagePriceDKK ?? 0,
      peakPriceDKK: Math.max(...last24.map(h => h.priceDKK)),
      avgCo2:       window?.averageCo2 ?? 0,
      priceArea:    area,
      strategy:     strategy === "Cheapest" ? 0 : 1,
    }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["stats"] }),
  });

  // Only show the most recent 24 hours
  const last24 = merged.length === 0
    ? []
    : [...merged]
        .sort((a, b) => (a.hourStart < b.hourStart ? -1 : a.hourStart > b.hourStart ? 1 : 0))
        .slice(-24);

  const avgOere = last24.length ? Math.round(last24.reduce((s, d) => s + d.priceDKK, 0) / last24.length * 100) : 0;
  const avgCo2  = last24.length ? last24.reduce((s, d) => s + d.co2PerKwh, 0) / last24.length : 0;

  // Merge historical + forecast into one chart dataset
  const chartData = useMemo(() => {
    const hist = last24.map(h => {
      const rec = recommendations.find(r => r.hourStart === h.hourStart);
      const inWin = window &&
        new Date(h.hourStart) >= new Date(window.windowStart) &&
        new Date(h.hourStart) < new Date(window.windowEnd);
      return {
        hourStart: h.hourStart,
        priceOere: toOere(h.priceDKK),
        co2PerKwh: h.co2PerKwh,
        forecastOere: null as number | null,
        isRecommended: rec?.isRecommended ?? false,
        inWindow: inWin ?? false,
        isForecast: false,
      };
    });
    const fc = forecast.map(f => ({
      hourStart:     f.hourStart,
      priceOere:     null as number | null,
      co2PerKwh:     0,
      forecastOere:  toOere(f.forecastedPriceDKK),
      isRecommended: false,
      inWindow:      false,
      isForecast:    true,
    }));
    return [...hist, ...fc];
  }, [last24, forecast, recommendations, window]);

  const isCheapest = strategy === "Cheapest";

  return (
    <div className="app">
      <header>
        <span className="eyebrow">Elbil · Energinet · Danmark</span>
        <h1>SmartCharger ⚡</h1>
        <p>Lad billigt. Lad grønt. Altid det rigtige tidspunkt.</p>
      </header>

      <div className="strategy-toggle">
        <button className={isCheapest ? "active" : ""} onClick={() => setStrategy("Cheapest")}>
          💰 Billigst
        </button>
        <button className={!isCheapest ? "active green" : "green"} onClick={() => setStrategy("Greenest")}>
          🌿 Grønnest
        </button>
      </div>

      <div className="controls">
        <label>
          Prisområde:
          <select value={area} onChange={e => setArea(e.target.value)}>
            {AREAS.map(a => <option key={a}>{a}</option>)}
          </select>
        </label>
        <label>
          Timer der skal oplades:
          <input type="number" min={1} max={12} value={hours}
            onChange={e => setHours(Number(e.target.value))} />
        </label>
        <label>
          Bilen skal køre kl.:
          <input
            type="time"
            value={deadline}
            className={deadlineError ? "input-error" : ""}
            onChange={e => setDeadline(e.target.value)}
          />
        </label>
      </div>

      {deadlineError && <p className="error">⚠️ {deadlineError}</p>}
      {isError      && <p className="error">Kunne ikke hente data — prøv igen om lidt.</p>}

      {isLoading ? (
        <p className="loading">Henter priser…</p>
      ) : (
        <>
          {window && (
            <div className={`window-banner ${!isCheapest ? "green-banner" : ""}`}>
              <strong>Bedste {isCheapest ? "billigste" : "grønneste"} vindue:</strong>{" "}
              {fmt(window.windowStart)} – {fmt(window.windowEnd)}{" · "}
              {isCheapest
                ? `gns. ${toOere(window.averagePriceDKK)} øre/kWh`
                : `gns. ${window.averageCo2.toFixed(0)} g CO₂/kWh`}
              <button
                className="save-btn"
                onClick={() => saveSession.mutate()}
                disabled={saveSession.isPending || saveSession.isSuccess}
              >
                {saveSession.isSuccess ? "✓ Gemt" : saveSession.isPending ? "…" : "Gem opladning"}
              </button>
            </div>
          )}

          <div className="summary">
            <div className="card">
              <span>{avgOere} øre</span>
              <small>Gns. spotpris/kWh</small>
            </div>
            <div className="card green">
              <span>{avgCo2.toFixed(0)} g</span>
              <small>Gns. CO₂/kWh</small>
            </div>
            {window && (
              <div className={`card ${isCheapest ? "blue" : "emerald"}`}>
                <span>{fmt(window.windowStart)}</span>
                <small>Start optimalt vindue</small>
              </div>
            )}
          </div>

          <div className="chart-wrapper">
          <ResponsiveContainer width="100%" height={280}>
            <ComposedChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 50 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis
                dataKey="hourStart"
                tickFormatter={fmt}
                angle={-45}
                textAnchor="end"
                interval={3}
                height={60}
              />
              <YAxis
                yAxisId="price"
                tickFormatter={v => `${v}ø`}
                width={42}
                tick={{ fontSize: 11 }}
              />
              <YAxis
                yAxisId="co2"
                orientation="right"
                tickFormatter={v => `${v}g`}
                domain={["auto", "auto"]}
                width={38}
                tick={{ fontSize: 11 }}
              />
              <Tooltip
                formatter={(v, name) => {
                  const val = Number(v);
                  return name === "CO₂"
                    ? [`${val.toFixed(0)} g CO₂/kWh`, "CO₂"]
                    : [`${val} øre/kWh`, "Spotpris"];
                }}
                labelFormatter={l => new Date(l).toLocaleString("da-DK")}
              />
              <Legend verticalAlign="bottom" height={36} wrapperStyle={{ paddingTop: "12px" }} />
              <ReferenceLine yAxisId="price" y={avgOere} stroke="#888" strokeDasharray="4 4" />
              <Bar yAxisId="price" dataKey="priceOere" radius={[4, 4, 0, 0]} name="Spotpris">
                {chartData.map((entry, i) => (
                  <Cell key={i} fill={
                    entry.inWindow      ? (isCheapest ? "#22c55e" : "#10b981") :
                    entry.isRecommended ? "#86efac" : "#64748b"
                  } />
                ))}
              </Bar>
              <Bar yAxisId="price" dataKey="forecastOere" radius={[4,4,0,0]}
                name="Prognose" fill="rgba(99,102,241,0.5)" />
              <Line yAxisId="co2" type="monotone" dataKey="co2PerKwh"
                stroke="#f59e0b" strokeWidth={2} dot={false} name="CO₂" />
            </ComposedChart>
          </ResponsiveContainer>
          </div>

          <p className="legend">
            <span className="dot green" /> Optimalt vindue &nbsp;
            <span className="dot lightgreen" /> Anbefalet time &nbsp;
            <span className="dot grey" /> Øvrige timer &nbsp;
            <span className="dot indigo" /> Prognose
          </p>

          {stats && stats.totalSessions > 0 && (
            <div className="stats-section">
            <hr className="section-divider" />
              <span className="stats-eyebrow">Din besparelse</span>
              <div className="stats-grid">
                <div className="stat-card">
                  <span>{stats.totalSessions}</span>
                  <small>Opladninger gemt</small>
                </div>
                <div className="stat-card green">
                  <span>{Math.round(stats.totalSavingsDKK * 100)} øre</span>
                  <small>Sparet i alt</small>
                </div>
                <div className="stat-card blue">
                  <span>{Math.round(stats.avgSavingPerSession * 100)} øre</span>
                  <small>Gns. pr. opladning</small>
                </div>
                <div className="stat-card emerald">
                  <span>{stats.totalCo2Saved.toFixed(0)} g</span>
                  <small>CO₂ undgået</small>
                </div>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}

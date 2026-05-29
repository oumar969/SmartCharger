import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid,
  Cell, ResponsiveContainer, ReferenceLine
} from "recharts";
import axios from "axios";
import "./App.css";

interface Recommendation {
  hourStart: string;
  priceDKK: number;
  isRecommended: boolean;
}

interface ChargeWindow {
  windowStart: string;
  windowEnd: string;
  totalCostDKK: number;
  averagePriceDKK: number;
  hours: Recommendation[];
}

const API = "http://localhost:5000/api/elspot";
const AREAS = ["DK1", "DK2"];

function fmt(iso: string) {
  return new Date(iso).toLocaleTimeString("da-DK", { hour: "2-digit", minute: "2-digit" });
}

export default function App() {
  const [hours, setHours] = useState(4);
  const [area, setArea] = useState("DK2");
  const [deadline, setDeadline] = useState("07:00");

  const deadlineParam = (() => {
    const [h, m] = deadline.split(":").map(Number);
    const d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(h, m, 0, 0);
    return d.toISOString();
  })();

  const { data: recommendations = [], isLoading, isError } = useQuery<Recommendation[]>({
    queryKey: ["recommendations", hours, area],
    queryFn: () =>
      axios.get(`${API}/recommendations?hours=${hours}&area=${area}`).then(r => r.data),
  });

  const { data: window } = useQuery<ChargeWindow>({
    queryKey: ["window", hours, area, deadlineParam],
    queryFn: () =>
      axios.get(`${API}/window?hours=${hours}&area=${area}&deadline=${encodeURIComponent(deadlineParam)}`).then(r => r.data),
  });

  const avgPrice = recommendations.length
    ? recommendations.reduce((s, d) => s + d.priceDKK, 0) / recommendations.length
    : 0;

  return (
    <div className="app">
      <header>
        <h1>⚡ SmartCharger</h1>
        <p>Opladningsanbefaling baseret på live elpriser fra Energinet</p>
      </header>

      <div className="controls">
        <label>
          Prisområde:
          <select value={area} onChange={(e) => setArea(e.target.value)}>
            {AREAS.map((a) => <option key={a}>{a}</option>)}
          </select>
        </label>
        <label>
          Timer der skal oplades:
          <input
            type="number" min={1} max={12} value={hours}
            onChange={(e) => setHours(Number(e.target.value))}
          />
        </label>
        <label>
          Bilen skal køre kl.:
          <input
            type="time" value={deadline}
            onChange={(e) => setDeadline(e.target.value)}
          />
        </label>
      </div>

      {isError && <p className="error">Kunne ikke hente priser — prøv igen om lidt.</p>}

      {isLoading ? (
        <p className="loading">Henter priser…</p>
      ) : (
        <>
          {window && (
            <div className="window-banner">
              <strong>Bedste sammenhængende vindue:</strong>{" "}
              {fmt(window.windowStart)} – {fmt(window.windowEnd)}{" "}
              · gns. {window.averagePriceDKK.toFixed(4)} kr/kWh
              · total {window.totalCostDKK.toFixed(4)} kr/kWh
            </div>
          )}

          <div className="summary">
            <div className="card green">
              <span>{recommendations.filter(r => r.isRecommended).length} timer</span>
              <small>Billigste ladeperioder</small>
            </div>
            <div className="card">
              <span>{avgPrice.toFixed(2)} kr/kWh</span>
              <small>Gennemsnitspris</small>
            </div>
            {window && (
              <div className="card blue">
                <span>{fmt(window.windowStart)}</span>
                <small>Start optimalt vindue</small>
              </div>
            )}
          </div>

          <ResponsiveContainer width="100%" height={320}>
            <BarChart data={recommendations} margin={{ top: 8, right: 16, left: 0, bottom: 60 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis
                dataKey="hourStart"
                tickFormatter={fmt}
                angle={-45} textAnchor="end"
              />
              <YAxis tickFormatter={(v) => `${v} kr`} />
              <Tooltip
                formatter={(v: number) => [`${v.toFixed(4)} kr/kWh`, "Pris"]}
                labelFormatter={(l) => new Date(l).toLocaleString("da-DK")}
              />
              <ReferenceLine y={avgPrice} stroke="#888" strokeDasharray="4 4" label="Gns." />
              <Bar dataKey="priceDKK" radius={[4, 4, 0, 0]}>
                {recommendations.map((entry, i) => {
                  const inWindow = window &&
                    new Date(entry.hourStart) >= new Date(window.windowStart) &&
                    new Date(entry.hourStart) < new Date(window.windowEnd);
                  return <Cell key={i} fill={inWindow ? "#22c55e" : entry.isRecommended ? "#86efac" : "#64748b"} />;
                })}
              </Bar>
            </BarChart>
          </ResponsiveContainer>

          <p className="legend">
            <span className="dot green" /> Optimalt vindue &nbsp;
            <span className="dot lightgreen" /> Billig time &nbsp;
            <span className="dot grey" /> Dyrere time
          </p>
        </>
      )}
    </div>
  );
}

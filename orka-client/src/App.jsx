import { Routes, Route, Navigate } from 'react-router-dom';
import Landing from './pages/Landing';
import Login from './pages/Login';
import Register from './pages/Register';
import AppDashboard from './pages/AppDashboard';
import { Toaster } from 'react-hot-toast';

function ProtectedRoute({ children }) {
  const token = localStorage.getItem('orka_token');
  if (!token) return <Navigate to="/login" replace />;
  return children;
}

function App() {
  return (
    <>
      <Toaster
        position="top-right"
        toastOptions={{
          style: {
            background: '#101325',
            color: '#dde2f4',
            border: '1px solid #232844',
            fontSize: '13px',
            fontFamily: "'Plus Jakarta Sans', sans-serif",
          },
          success: {
            iconTheme: { primary: '#f0a500', secondary: '#101325' },
          },
          error: {
            iconTheme: { primary: '#ef4444', secondary: '#101325' },
          },
        }}
      />
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route
          path="/app/*"
          element={
            <ProtectedRoute>
              <AppDashboard />
            </ProtectedRoute>
          }
        />
      </Routes>
    </>
  );
}

export default App;

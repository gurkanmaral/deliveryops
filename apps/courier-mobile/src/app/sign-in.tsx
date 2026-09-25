import { Button } from '@/components/ui';
import { useAuth } from '@/features/auth/auth-context';
import { ApiError } from '@/shared/api';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

export default function SignInScreen() {
  const { signIn } = useAuth();
  const [email, setEmail] = useState('courier@demo.local');
  const [password, setPassword] = useState('CourierDemo123!');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const submit = async () => {
    setError(''); setLoading(true);
    try { await signIn(email, password); }
    catch (reason) { setError(reason instanceof ApiError ? reason.message : 'Sunucuya ulaşılamadı. API adreslerini kontrol edin.'); }
    finally { setLoading(false); }
  };
  return <SafeAreaView style={styles.safe}><KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.container}>
    <View style={styles.brand}>
      <View style={styles.brandLine}><View style={styles.logo}><Ionicons name="bicycle" size={22} color={colors.white} /></View><Text style={styles.kicker}>DeliveryOps</Text><View style={styles.livePill}><Text style={styles.liveText}>Kurye</Text></View></View>
      <Text style={styles.title}>Şehir senin{'\n'}rotan.</Text>
      <Text style={styles.description}>Vardiyanı yönet, paketleri üstlen ve teslimatı tek yerden ilerlet.</Text>
    </View>
    <View style={styles.form}>
      <Text style={styles.label}>E-posta</Text>
      <View style={styles.inputShell}><Ionicons name="mail-outline" size={18} color={colors.textMuted} /><TextInput accessibilityLabel="E-posta" testID="sign-in-email" autoCapitalize="none" autoComplete="email" keyboardType="email-address" value={email} onChangeText={setEmail} style={styles.input} placeholderTextColor={colors.textFaint} placeholder="ornek@firma.com" /></View>
      <Text style={styles.label}>Şifre</Text>
      <View style={styles.inputShell}><Ionicons name="lock-closed-outline" size={18} color={colors.textMuted} /><TextInput accessibilityLabel="Şifre" testID="sign-in-password" autoCapitalize="none" autoComplete="password" secureTextEntry returnKeyType="done" onSubmitEditing={submit} value={password} onChangeText={setPassword} style={styles.input} /></View>
      {error ? <View style={styles.errorBox}><Ionicons name="alert-circle" size={16} color={colors.danger} /><Text style={styles.error}>{error}</Text></View> : null}
      <View style={styles.submit}><Button size="lg" title="Giriş yap" onPress={submit} loading={loading} disabled={!email.trim() || !password} icon={<Ionicons name="arrow-forward" size={18} color={colors.white} />} /></View>
      <View style={styles.secure}><Ionicons name="shield-checkmark-outline" size={14} color={colors.textMuted} /><Text style={styles.secureText}>Oturum bilgilerin cihazda şifreli saklanır.</Text></View>
    </View>
  </KeyboardAvoidingView></SafeAreaView>;
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  container: { flex: 1, justifyContent: 'center', paddingHorizontal: 20, gap: 28 },
  brand: { gap: 12 },
  brandLine: { flexDirection: 'row', alignItems: 'center', gap: 10, marginBottom: 14 },
  logo: { width: 40, height: 40, borderRadius: 12, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center', shadowColor: colors.primary, shadowOpacity: 0.35, shadowRadius: 10, shadowOffset: { width: 0, height: 4 } },
  kicker: { color: colors.text, fontWeight: '700', fontSize: 17, letterSpacing: -0.3 },
  livePill: { paddingHorizontal: 8, paddingVertical: 3, backgroundColor: colors.primarySoft, borderRadius: 999 },
  liveText: { color: colors.primaryDark, fontSize: 12, fontWeight: '600' },
  title: { color: colors.text, fontWeight: '700', fontSize: 38, lineHeight: 42, letterSpacing: -1.4 },
  description: { color: colors.textMuted, fontSize: 16, lineHeight: 23, maxWidth: 320 },
  form: { backgroundColor: colors.surface, borderRadius: 22, padding: 18, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.borderStrong, gap: 8, shadowColor: colors.shadow, shadowOpacity: 0.06, shadowRadius: 20, shadowOffset: { width: 0, height: 8 }, elevation: 2 },
  label: { color: colors.textSecondary, fontWeight: '500', fontSize: 13, marginTop: 4 },
  inputShell: { height: 52, flexDirection: 'row', alignItems: 'center', gap: 10, borderWidth: 1, borderColor: colors.borderStrong, backgroundColor: colors.surface, borderRadius: 14, paddingHorizontal: 14 },
  input: { flex: 1, height: 50, color: colors.text, fontSize: 16 },
  errorBox: { flexDirection: 'row', gap: 8, padding: 10, borderRadius: 12, backgroundColor: colors.dangerSoft, marginTop: 4 },
  error: { flex: 1, color: colors.danger, fontSize: 13, lineHeight: 18 },
  submit: { marginTop: 10 },
  secure: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 6, marginTop: 6 },
  secureText: { color: colors.textMuted, fontSize: 12 },
});

import { useEffect, useRef } from 'react'
import { Bike } from 'lucide-react'
import * as maplibregl from 'maplibre-gl'
import type { Map as MapLibreMap, Marker } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import type { CourierLocation } from '../api/useCourierLocations'

export function CourierMap({ locations }: { locations: CourierLocation[] }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const markersRef = useRef<Map<string, Marker>>(new Map())
  const fittedRef = useRef(false)

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return
    const map = new maplibregl.Map({
      container: containerRef.current,
      style: 'https://demotiles.maplibre.org/style.json',
      center: [29.02, 41.02],
      zoom: 9.5,
      attributionControl: false,
    })
    map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right')
    map.addControl(new maplibregl.AttributionControl({ compact: true }))
    mapRef.current = map
    const markers = markersRef.current
    return () => { map.remove(); mapRef.current = null; markers.clear() }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const activeIds = new Set(locations.map(item => item.courierId))
    for (const [id, marker] of markersRef.current) {
      if (!activeIds.has(id)) { marker.remove(); markersRef.current.delete(id) }
    }
    for (const location of locations) {
      let marker = markersRef.current.get(location.courierId)
      const color = location.isStale ? '#a1a1aa' : location.deliveryStatus === 'Delivering' || location.deliveryStatus === 2 ? '#3b82f6' : '#22a55a'
      if (!marker) {
        const element = document.createElement('div')
        element.className = 'courier-map-marker'
        element.innerHTML = '<span>↗</span>'
        marker = new maplibregl.Marker({ element })
          .setPopup(new maplibregl.Popup({ offset: 22 }).setText(location.courierName))
          .setLngLat([location.longitude, location.latitude]).addTo(map)
        markersRef.current.set(location.courierId, marker)
      }
      marker.setLngLat([location.longitude, location.latitude])
      marker.getElement().style.backgroundColor = color
      marker.getPopup()?.setText(`${location.courierName}${location.isStale ? ' • konum eski' : ''}`)
    }
    if (!fittedRef.current && locations.length > 0) {
      const bounds = new maplibregl.LngLatBounds()
      locations.forEach(item => bounds.extend([item.longitude, item.latitude]))
      map.fitBounds(bounds, { padding: 55, maxZoom: 13 })
      fittedRef.current = true
    }
  }, [locations])

  return <><div ref={containerRef} className="courier-map" />
    {locations.length === 0 && <div className="map-empty"><Bike size={24} /><span>Henüz kurye konumu alınmadı</span></div>}
  </>
}

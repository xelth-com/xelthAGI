Set-Location 'C:\Users\Dmytro\xelthAGI\server'
$token = 'xlt_mjyy9urm_myghzs3m_9ac7f59002ff39adfc85501650b42edd_26aa0b98cc1828c8c3f7ca06f2dd9a46626f7c2b0cc930eab8ee19e4b064d926021ea70624dc73e63d7ce25c7f4b32b2d31a37e7cd4d6b426fa14027474395af0728b1f8b1b282456b6da0b42104d0f9_aebf0bf1e0ad30e83847cc5b9c282b6837fd824a745a0b08e9fdb522d4788f4c'
$result = node -e "const s=require('./src/authService'); console.log(JSON.stringify(s.validateToken('$token')))"
Write-Host $result

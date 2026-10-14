package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.repository.BuildingMasterRepository;
import com.example.jamkkaebi.relic.repository.RelicMasterRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * 콘텐츠 마스터를 읽는다. 유물 9행·건물 3행뿐이라 캐시를 두지 않고 그때그때 조회한다.
 *
 */
@Service
@Transactional(readOnly = true)
public class RelicCatalog {

    private final RelicMasterRepository relicRepository;
    private final BuildingMasterRepository buildingRepository;

    public RelicCatalog(RelicMasterRepository relicRepository,
                           BuildingMasterRepository buildingRepository) {
        this.relicRepository = relicRepository;
        this.buildingRepository = buildingRepository;
    }

    // 도감·작업대 순서대로 전부
    public List<RelicMaster> allRelics() {
        return relicRepository.findAllByOrderByDisplayOrderAsc();
    }

    public RelicMaster relic(Integer relicId) {
        return findRelic(relicId)
                .orElseThrow(() -> new BusinessException(ErrorCode.RELIC_NOT_OWNED));
    }

    // 없는 유물 번호 = 보유하지 않은 유물로 취급
    public Optional<RelicMaster> findRelic(Integer relicId) {
        return relicId == null ? Optional.empty() : relicRepository.findById(relicId);
    }

    public Map<Integer, RelicMaster> relics(Collection<Integer> relicIds) {
        if (relicIds.isEmpty()) {
            return Map.of();
        }
        return relicRepository.findAllById(relicIds).stream()
                .collect(Collectors.toMap(RelicMaster::getId, Function.identity()));
    }

    // 한 시대의 유물 번호들
    public List<Integer> relicIdsOf(Era era) {
        return allRelics().stream()
                .filter(relic -> relic.getEra() == era)
                .map(RelicMaster::getId)
                .toList();
    }

    public List<BuildingMaster> allBuildings() {
        return buildingRepository.findAll();
    }

    // 없는 건물 번호 = 보유하지 않은 건물로 취급
    public BuildingMaster building(Integer buildingId) {
        return buildingRepository.findById(buildingId)
                .orElseThrow(() -> new BusinessException(ErrorCode.BUILDING_NOT_OWNED));
    }

    public Optional<BuildingMaster> buildingOf(Era era) {
        return buildingRepository.findByEra(era);
    }

    public Map<Integer, BuildingMaster> buildings(Collection<Integer> buildingIds) {
        if (buildingIds.isEmpty()) {
            return Map.of();
        }
        return buildingRepository.findAllById(buildingIds).stream()
                .collect(Collectors.toMap(BuildingMaster::getId, Function.identity()));
    }
}

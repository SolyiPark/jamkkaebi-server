package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.repository.ArtifactMasterRepository;
import com.example.jamkkaebi.artifact.repository.BuildingMasterRepository;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
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
public class ArtifactCatalog {

    private final ArtifactMasterRepository artifactRepository;
    private final BuildingMasterRepository buildingRepository;

    public ArtifactCatalog(ArtifactMasterRepository artifactRepository,
                           BuildingMasterRepository buildingRepository) {
        this.artifactRepository = artifactRepository;
        this.buildingRepository = buildingRepository;
    }

    // 도감·작업대 순서대로 전부
    public List<ArtifactMaster> allArtifacts() {
        return artifactRepository.findAllByOrderByDisplayOrderAsc();
    }

    public ArtifactMaster artifact(Integer artifactId) {
        return findArtifact(artifactId)
                .orElseThrow(() -> new BusinessException(ErrorCode.ARTIFACT_NOT_OWNED));
    }

    // 없는 유물 번호 = 보유하지 않은 유물로 취급
    public Optional<ArtifactMaster> findArtifact(Integer artifactId) {
        return artifactId == null ? Optional.empty() : artifactRepository.findById(artifactId);
    }

    public Map<Integer, ArtifactMaster> artifacts(Collection<Integer> artifactIds) {
        if (artifactIds.isEmpty()) {
            return Map.of();
        }
        return artifactRepository.findAllById(artifactIds).stream()
                .collect(Collectors.toMap(ArtifactMaster::getId, Function.identity()));
    }

    // 한 시대의 유물 번호들
    public List<Integer> artifactIdsOf(Era era) {
        return allArtifacts().stream()
                .filter(artifact -> artifact.getEra() == era)
                .map(ArtifactMaster::getId)
                .toList();
    }

    public List<BuildingMaster> allBuildings() {
        return buildingRepository.findAll();
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

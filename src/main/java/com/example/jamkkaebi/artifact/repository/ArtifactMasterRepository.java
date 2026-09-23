package com.example.jamkkaebi.artifact.repository;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface ArtifactMasterRepository extends JpaRepository<ArtifactMaster, Integer> {

    List<ArtifactMaster> findAllByOrderByDisplayOrderAsc();
}
